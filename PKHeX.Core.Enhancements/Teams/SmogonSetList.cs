using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PKHeX.Core.Enhancements
{
    /// <summary>
    /// Parser for Smogon webpage <see cref="ShowdownSet"/> data.
    /// </summary>
    public class SmogonSetList
    {
        public readonly bool Valid;
        public readonly string URL;
        public readonly string Species;
        public readonly string Form;
        public readonly string ShowdownSpeciesName;
        public readonly string Page;
        public readonly bool LetsGo;
        public readonly bool BDSP;
        public readonly List<string> SetFormat = new();
        public readonly List<string> SetName = new();
        public readonly List<string> SetConfig = new();
        public readonly List<string> SetText = new();
        public readonly List<ShowdownSet> Sets = new();

        public static readonly string[] IllegalFormats =
        {
            "Almost Any Ability", // Generates illegal abilities
            "BH", // Balanced Hackmons
            "Mix and Mega", // Assumes pokemon can mega evolve that cannot
            "STABmons", // Generates illegal movesets
            "National Dex", // Adds Megas to Generation VIII
            "National Dex AG" // Adds Megas to Generation VIII
        };

        public string Summary => AlertText(ShowdownSpeciesName, SetText.Count, GetTitles());

        public SmogonSetList(PKM pk)
        {
            var baseURL = GetBaseURL(pk.GetType().Name);
            if (string.IsNullOrWhiteSpace(baseURL))
            {
                URL = Species = Form = ShowdownSpeciesName = Page = string.Empty;
                return;
            }

            var set = new ShowdownSet(pk);
            Species = GameInfo.GetStrings("en").Species[pk.Species];
            Form = ConvertFormToURLForm(set.FormName, Species);
            var psform = ConvertFormToShowdown(set.FormName, set.Species);

            URL = GetURL(Species, Form, baseURL);
            Page = NetUtil.GetPageText(URL);

            LetsGo = pk is PB7;
            BDSP = pk is PB8;
            Valid = true;
            ShowdownSpeciesName = GetShowdownName(Species, psform);

            LoadSetsFromPage();
        }

        private static string GetShowdownName(string species, string form)
        {
            if (string.IsNullOrWhiteSpace(form) || ShowdownUtil.IsInvalidForm(form))
                return species;
            return $"{species}-{form}";
        }

        private void LoadSetsFromPage()
        {
            var split1 = Page.Split(new[] { "\",\"abilities\":" }, StringSplitOptions.None);
            var format = "";
            for (int i = 1; i < split1.Length; i++)
            {
                var shiny = split1[i - 1].Contains("\"shiny\":true");
                if (split1[i - 1].Contains("\"format\":\""))
                {
                    format = split1[i - 1][
                        (
                            split1[i - 1].IndexOf("\"format\":\"", StringComparison.Ordinal)
                            + "\"format\":\"".Length
                        )..
                    ].Split('\"')[0];
                }

                if (IllegalFormats.Any(s => s.Equals(format, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (LetsGo != format.StartsWith("LGPE", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (BDSP != format.StartsWith("BDSP", StringComparison.OrdinalIgnoreCase))
                    continue;

                var level = format.StartsWith("LC") ? 5 : 100;
                if (!split1[i - 1].Contains("\"name\":"))
                    continue;

                var name = split1[i - 1][
                    (
                        split1[i - 1].LastIndexOf("\"name\":\"", StringComparison.Ordinal)
                        + "\"name\":\"".Length
                    )..
                ].Split('\"')[0];
                var setSpecies = split1[i - 1][
                    (
                        split1[i - 1].LastIndexOf("\"pokemon\":\"", StringComparison.Ordinal)
                        + "\"pokemon\":\"".Length
                    )..
                ].Split('\"')[0];
                SetFormat.Add(format);
                SetName.Add(name);

                if (!split1[i - 1].Contains("\"level\":0,") && split1[i - 1].Contains("\"level\":"))
                    _ = int.TryParse(
                        split1[i - 1].Split(new[] { "\"level\":" }, StringSplitOptions.None)[
                            1
                        ].Split(',')[0],
                        out level
                    );

                var split2 = split1[i].Split(new[] { "\"]}" }, StringSplitOptions.None);
                var tmp = split2[0];
                SetConfig.Add(tmp);

                var morphed = ConvertSetToShowdown(tmp, setSpecies, shiny, level);
                SetText.Add(morphed);

                var converted = new ShowdownSet(morphed);
                Sets.Add(converted);
            }
        }

        private static string GetBaseURL(string type)
        {
            return type switch
            {
                nameof(PK1) => "https://www.smogon.com/dex/rb/pokemon",
                nameof(PK2) or nameof(SK2) => "https://www.smogon.com/dex/gs/pokemon",
                nameof(PK3)
                or nameof(CK3)
                or nameof(XK3)
                    => "https://www.smogon.com/dex/rs/pokemon",
                nameof(PK4) or nameof(BK4) => "https://www.smogon.com/dex/dp/pokemon",
                nameof(PK5) => "https://www.smogon.com/dex/bw/pokemon",
                nameof(PK6) => "https://www.smogon.com/dex/xy/pokemon",
                nameof(PK7) or nameof(PB7) => "https://www.smogon.com/dex/sm/pokemon",
                nameof(PK8) or nameof(PB8) => "https://www.smogon.com/dex/ss/pokemon",
                nameof(PK9) => "https://www.smogon.com/dex/sv/pokemon",
                _ => string.Empty,
            };
        }

        private static string ConvertSetToShowdown(
            string set,
            string species,
            bool shiny,
            int level
        )
        {
            var result = GetSetLines(set, species, shiny, level);
            return string.Join(Environment.NewLine, result);
        }

        private static readonly string[] statNames = { "HP", "Atk", "Def", "SpA", "SpD", "Spe" };

        private static IEnumerable<string> GetSetLines(
            string set,
            string species,
            bool shiny,
            int level
        )
        {
            TryGetToken(set, "\"items\":[\"", "\"", out var item);
            TryGetToken(set, "\"moveslots\":", ",\"evconfigs\":", out var movesets);
            TryGetToken(set, "\"evconfigs\":[{", "}],\"ivconfigs\":", out var evstr);
            TryGetToken(set, "\"ivconfigs\":[{", "}],\"natures\":", out var ivstr);
            TryGetToken(set, "\"natures\":[\"", "\"", out var nature);
            TryGetToken(set, "\"teratypes\":[\"", "\"", out var teratype);

            if (teratype != null && teratype.StartsWith(']'))
                teratype = null;

            var evs = ParseEVIVs(evstr, false);
            var ivs = ParseEVIVs(ivstr, true);
            var ability = set[1] == ']' ? string.Empty : set.Split('\"')[1];

            if (item == "No Item") // LGPE actually lists an item, RBY sets have an empty [].
                item = string.Empty;

            var result = new List<string>(9)
            {
                item.Length == 0 ? species : $"{species} @ {item}",
            };
            if (level != 100)
                result.Add($"Level: {level}");
            if (shiny)
                result.Add("Shiny: Yes");
            if (!string.IsNullOrWhiteSpace(ability))
                result.Add($"Ability: {ability}");
            if (!string.IsNullOrWhiteSpace(teratype))
                result.Add($"Tera Type: {teratype}");
            if (evstr.Length >= 3)
                result.Add(
                    $"EVs: {string.Join(" / ", statNames.Select((z, i) => $"{evs[i]} {z}"))}"
                );
            if (ivstr.Length >= 3)
                result.Add(
                    $"IVs: {string.Join(" / ", statNames.Select((z, i) => $"{ivs[i]} {z}"))}"
                );
            if (!string.IsNullOrWhiteSpace(nature))
                result.Add($"{nature} Nature");

            result.AddRange(GetMoves(movesets).Select(move => $"- {move}"));
            return result;
        }

        /// <summary>
        /// Tries to rip out a substring between the provided <see cref="prefix"/> and <see cref="suffix"/>.
        /// </summary>
        /// <param name="line">Line</param>
        /// <param name="prefix">Prefix</param>
        /// <param name="suffix">Suffix</param>
        /// <param name="result">Substring within prefix-suffix.</param>
        /// <returns>True if found a substring, false if no prefix found.</returns>
        private static bool TryGetToken(
            string line,
            string prefix,
            string suffix,
            out string result
        )
        {
            var prefixStart = line.IndexOf(prefix, StringComparison.Ordinal);
            if (prefixStart < 0)
            {
                result = string.Empty;
                return false;
            }
            prefixStart += prefix.Length;

            var suffixStart = line.IndexOf(suffix, prefixStart, StringComparison.Ordinal);
            if (suffixStart < 0)
                suffixStart = line.Length;

            result = line[prefixStart..suffixStart];
            return true;
        }

        private static IEnumerable<string> GetMoves(string movesets)
        {
            var moves = new List<string>();
            var slots = movesets.Split(new[] { "],[" }, StringSplitOptions.None);
            foreach (var slot in slots)
            {
                var choices = slot.Split(new[] { "\"move\":\"" }, StringSplitOptions.None)
                    .Skip(1)
                    .ToArray();
                foreach (var choice in choices)
                {
                    var move = GetMove(choice);
                    if (moves.Contains(move))
                        continue;
                    if (move.Equals("Hidden Power", StringComparison.OrdinalIgnoreCase))
                        move =
                            $"{move} [{choice.Split(new[] { "\"type\":\"" }, StringSplitOptions.None)[1].Split('\"')[0]}]";
                    moves.Add(move);
                    break;
                }

                if (moves.Count == 4)
                    break;
            }

            static string GetMove(string s) => s.Split('"')[0];
            return moves;
        }

        private static string[] ParseEVIVs(string liststring, bool iv)
        {
            string[] ivdefault = { "31", "31", "31", "31", "31", "31" };
            string[] evdefault = { "0", "0", "0", "0", "0", "0" };
            var val = iv ? ivdefault : evdefault;
            if (string.IsNullOrWhiteSpace(liststring))
                return val;

            string getStat(string v) =>
                liststring.Split(new[] { v }, StringSplitOptions.None)[1].Split(',')[0];
            val[0] = getStat("\"hp\":");
            val[1] = getStat("\"atk\":");
            val[2] = getStat("\"def\":");
            val[3] = getStat("\"spa\":");
            val[4] = getStat("\"spd\":");
            val[5] = getStat("\"spe\":");

            return val;
        }

        // Smogon Quirks
        private static string ConvertSpeciesToURLSpecies(string spec)
        {
            return spec switch
            {
                "Nidoran♂" => "nidoran-m",
                "Nidoran♀" => "nidoran-f",
                "Farfetch’d" => "farfetchd",
                "Flabébé" => "flabebe",
                "Sirfetch’d" => "sirfetchd",
                _ => spec,
            };
        }

        // Smogon Quirks
        private static string ConvertFormToURLForm(string form, string spec)
        {
            return spec switch
            {
                "Necrozma" when form == "Dusk" => "dusk_mane",
                "Necrozma" when form == "Dawn" => "dawn_wings",
                "Oricorio" when form == "Pa’u" => "pau",
                "Darmanitan" when form == "Galarian Standard" => "galar",
                "Meowstic" when form.Length == 0 => "m",
                "Gastrodon" => "",
                "Vivillon" => "",
                "Sawsbuck" => "",
                "Deerling" => "",
                "Furfrou" => "",
                _ => form,
            };
        }

        private static string ConvertFormToShowdown(string form, int spec)
        {
            if (form.Length == 0)
            {
                return spec switch
                {
                    (int)Core.Species.Minior => "Meteor",
                    _ => form,
                };
            }

            switch (spec)
            {
                case (int)Core.Species.Basculin when form == "Blue":
                    return "Blue-Striped";
                case (int)Core.Species.Vivillon when form == "Poké Ball":
                    return "Pokeball";
                case (int)Core.Species.Zygarde:
                    form = form.Replace("-C", string.Empty);
                    return form.Replace("50%", string.Empty);
                case (int)Core.Species.Minior:
                    if (form.StartsWith("M-"))
                        return "Meteor";
                    return form.Replace("C-", string.Empty);
                case (int)Core.Species.Necrozma when form == "Dusk":
                    return $"{form}-Mane";
                case (int)Core.Species.Necrozma when form == "Dawn":
                    return $"{form}-Wings";

                case (int)Core.Species.Furfrou:
                case (int)Core.Species.Greninja:
                case (int)Core.Species.Rockruff:
                    return string.Empty;

                case (int)Core.Species.Polteageist:
                case (int)Core.Species.Sinistea:
                    return form == "Antique" ? form : string.Empty;

                default:
                    if (Totem_USUM.Contains(spec) && form == "Large")
                        return Totem_Alolan.Contains(spec) && spec != (int)Core.Species.Mimikyu
                            ? "Alola-Totem"
                            : "Totem";
                    return form.Replace(' ', '-');
            }
        }

        internal static readonly HashSet<int> Totem_Alolan =
            new()
            {
                020, // Raticate (Normal, Alolan, Totem)
                105, // Marowak (Normal, Alolan, Totem)
                778, // Mimikyu (Normal, Busted, Totem, Totem_Busted)
            };

        internal static readonly HashSet<int> Totem_USUM =
            new()
            {
                020, // Raticate
                735, // Gumshoos
                758, // Salazzle
                754, // Lurantis
                738, // Vikavolt
                778, // Mimikyu
                784, // Kommo-o
                105, // Marowak
                752, // Araquanid
                777, // Togedemaru
                743, // Ribombee
            };

        private static string GetURL(string speciesName, string form, string baseURL)
        {
            if (
                string.IsNullOrWhiteSpace(form)
                || (ShowdownUtil.IsInvalidForm(form) && form != "Crowned")
            ) // Crowned Formes have separate pages
            {
                var spec = ConvertSpeciesToURLSpecies(speciesName).ToLower();
                return $"{baseURL}/{spec}/";
            }

            var urlSpecies = ConvertSpeciesToURLSpecies(speciesName);
            {
                var spec = urlSpecies.ToLower();
                var f = form.ToLower();
                return $"{baseURL}/{spec}-{f}/";
            }
        }

        private Dictionary<string, List<string>> GetTitles()
        {
            var titles = new Dictionary<string, List<string>>();
            for (int i = 0; i < Sets.Count; i++)
            {
                var format = SetFormat[i];
                var name = SetName[i];
                if (titles.TryGetValue(format, out var list))
                    list.Add(name);
                else
                    titles.Add(format, new List<string> { name });
            }

            return titles;
        }

        private static string AlertText(
            string showdownSpec,
            int count,
            Dictionary<string, List<string>> titles
        )
        {
            var sb = new StringBuilder();
            sb.Append(showdownSpec).Append(':');
            sb.Append(Environment.NewLine);
            sb.Append(Environment.NewLine);
            foreach (var entry in titles)
            {
                sb.Append(entry.Key).Append(": ").Append(string.Join(", ", entry.Value));
                sb.Append(Environment.NewLine);
            }
            sb.Append(Environment.NewLine);
            sb.Append(count).Append(" sets generated for ").Append(showdownSpec);
            return sb.ToString();
        }
    }
}
