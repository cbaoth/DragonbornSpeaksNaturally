using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.IO;

namespace DSN {
    class FavoritesList : ISpeechRecognitionGrammarProvider {
        // Add a space before the word to prevent false matches (so "Iron Battleaxe" will not matches " Axe").
        private static readonly string DEFAULT_KNOWN_EQUIPMENT_TYPES
            = " Dagger; Mace; Sword; Axe; Battleaxe; Greatsword; Warhammer; Bow; Crossbow; Shield";

        private Configuration config;
        private Dictionary<Grammar, string> commandsByGrammar;

        private bool enabled;
        private bool useEquipHandPrefix;

        private bool omitHandSuffix = false;
        private string[] equipPhrasePrefix;
        private List<string> knownEquipmentTypes;

        private string[] leftHandSuffix;
        private string[] rightHandSuffix;
        private string[] bothHandsSuffix;

        private string mainHand;
        private string mainHandId;

        public FavoritesList(Configuration config) {
            this.config = config;
            commandsByGrammar = new Dictionary<Grammar, string>();

            enabled = config.Get("Favorites", "enabled", "1") == "1";
            useEquipHandPrefix = config.Get("Favorites", "useEquipHandPrefix", "0") == "1";

            knownEquipmentTypes = config.Get("Favorites", "knownEquipmentTypes", DEFAULT_KNOWN_EQUIPMENT_TYPES)
                .Split(';').Select((x) => x.ToLower()).ToList();
            if (knownEquipmentTypes.Count == 1 && knownEquipmentTypes[0].Length == 0) {
                knownEquipmentTypes.Clear();
                Trace.TraceInformation("Known Equipment Types Disabled");
            } else {
                // Sort in reverse order of string length to avoid prefix masking.
                // Consider "axe" and "battleaxe", the former is the prefix of the latter.
                // If the order is not correct, the latter will never have a chance to be matched.
                // Note: Adding leading space alleviates the problem, but some languages don't add spaces between words.
                knownEquipmentTypes.Sort((x, y) => y.Length - x.Length);
                Trace.TraceInformation("Known Equipment Types: \"{0}\"", string.Join("\", \"", knownEquipmentTypes.ToArray()));
            }

            leftHandSuffix = config.Get("Favorites", "equipLeftSuffix", "left")
                .Split(';').Select((x) => Phrases.normalize(x)).ToArray();
            rightHandSuffix = config.Get("Favorites", "equipRightSuffix", "right")
                .Split(';').Select((x) => Phrases.normalize(x)).ToArray();
            bothHandsSuffix = config.Get("Favorites", "equipBothSuffix", "both")
                .Split(';').Select((x) => Phrases.normalize(x)).ToArray();

            List<string> equipPrefixList = config.Get("Favorites", "equipPhrasePrefix", "equip")
                .Split(';').Select((x) => Phrases.normalize(x)).ToList();
            for (int i=equipPrefixList.Count-1; i>=0; i--) {
                if (equipPrefixList[i].Length == 0) {
                    equipPrefixList.RemoveAt(i);
                    omitHandSuffix = true;
                }
            }
            equipPhrasePrefix = equipPrefixList.ToArray();

            mainHand = config.Get("Favorites", "mainHand", "none");

            // Determine the main hand used when user didn't ask for a specific hand.
            //
            // If an initializer is used and the key name conflicts (such as bothHandsSuffix == "both"),
            // an System.ArgumentException will be thrown. So assigning values one by one is a safer way.
            var mainHandMap = new Dictionary<string, string> {
                // Comment of `mainHand` in `DragonbornSpeaksNaturally.SAMPLE.ini` said:
                // > Valid values are "right", "left", "both"
                // We should keep the compatibility to prevent user confusion.
                ["both"] = "0",
                ["right"] = "1",
                ["left"] = "2"
            };
            // Use assignment statements to avoid key conflicts.
            bothHandsSuffix.Select((x) => mainHandMap[x] = "0");
            rightHandSuffix.Select((x) => mainHandMap[x] = "1");
            leftHandSuffix.Select((x) => mainHandMap[x] = "2");

            if (mainHandMap.ContainsKey(mainHand)) {
                mainHandId = mainHandMap[mainHand];
            } else {
                // User does not specify the main hand. Equipped with both hands by default.
                mainHandId = "0";
            }
        }

        public string ProbableEquipmentType(string itemName)
        {
            //
            // NOTICE:
            //    1. Some languages (such as Chinese) don't add spaces between words.
            //       So the code such as `itemName.Split(' ')` will not work for them.
            //       Be aware of this when changing the code below.
            //
            //    2. knownEquipmentTypes should be sorted in reverse order of string length to avoid prefix masking.
            //       Consider "axe" and "battleaxe", the former is the prefix of the latter.
            //       If the order is not correct, the latter will never have a chance to be matched.
            //       Sorting is currently done in the constructor.
            //       Note: Adding leading space alleviates the problem, but some languages don't add spaces between words.
            //
            itemName = Phrases.normalize(itemName).ToLower();

            foreach (string type in knownEquipmentTypes) {
                if (itemName.Contains(type)) {
                    return type;
                }
            }
            return null;
        }

        public void BuildAndAddGrammar(string[] equipPrefix, string phrase, string command, bool isSingleHanded)
        {
            Choices equipPrefixChoice = new Choices(equipPrefix.ToArray());

            List<string> handsSuffix = new List<string>();
            handsSuffix.AddRange(bothHandsSuffix);
            handsSuffix.AddRange(rightHandSuffix);
            handsSuffix.AddRange(leftHandSuffix);
            Choices handChoice = new Choices(handsSuffix.ToArray());

            GrammarBuilder grammarBuilder = new GrammarBuilder();

            // Append hand choice prefix
            if (isSingleHanded && useEquipHandPrefix)
            {
                // Optional left/right. When excluded, try to equip to both hands
                grammarBuilder.Append(handChoice, 0, 1);
            }

            grammarBuilder.Append(equipPrefixChoice, omitHandSuffix ? 0 : 1, 1);
            grammarBuilder.Append(phrase);

            // Append hand choice suffix
            if (isSingleHanded && !useEquipHandPrefix)
            {
                // Optional left/right. When excluded, try to equip to both hands
                grammarBuilder.Append(handChoice, 0, 1);
            }

            Grammar grammar = new Grammar(grammarBuilder);
            grammar.Name = phrase;
            commandsByGrammar[grammar] = command;
        }

        // Locates and loads item name replacement maps
        // Returns dynamic map/dictionary or null when the replacement map files cannot be located
        public dynamic LoadItemNameMap()
        {
            string filepath = config.resolveFilePath("item-name-map.json");
            if(File.Exists(filepath))
            {
                return LoadItemNameMap(filepath);
            }
            return null;
        }

        // Returns a map/dictionary or throws exception when the file cannot be opened/read
        public dynamic LoadItemNameMap(string path)
        {
            var json = System.IO.File.ReadAllText(path);
            JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
            return jsonSerializer.Deserialize<dynamic>(json);
        }

        public string MaybeReplaceItemName(dynamic nameMap, string itemName)
        {
            if (nameMap == null)
                return itemName;

            try {
                return nameMap[itemName];
            } catch {
                return itemName;
            }
        }

        public void Update(string input) {
            if(!enabled) {
                return;
            }

            var firstEquipmentOfType = new Dictionary<string, string> { };

            dynamic itemNameMap = LoadItemNameMap();

            commandsByGrammar.Clear();
            string[] itemTokens = input.Split('|');
            foreach(string itemStr in itemTokens) {
                try
                {
                    if (itemStr.Length == 0) {
                        continue;
                    }

                    string[] tokens = itemStr.Split(',');
                    string itemName = tokens[0];
                    long formId = long.Parse(tokens[1]);
                    long itemId = long.Parse(tokens[2]);
                    bool isSingleHanded = int.Parse(tokens[3]) > 0;
                    int typeId = int.Parse(tokens[4]);

                    itemName = MaybeReplaceItemName(itemNameMap, itemName);
                    string command = formId + ";" + itemId + ";" + typeId + ";";

                    BuildAndAddGrammar(equipPhrasePrefix, Phrases.normalize(itemName), command, isSingleHanded);

                    // Are we looking at an equipment of some sort?
                    // Record the first item of a specific weapon type
                    string equipmentType = ProbableEquipmentType(itemName);
                    if(equipmentType != null && !firstEquipmentOfType.ContainsKey(equipmentType))
                    {
                        Trace.TraceInformation("ProbableEquipmentType: {0} -> {1}", itemName, equipmentType);
                        BuildAndAddGrammar(equipPhrasePrefix, Phrases.normalize(equipmentType), command, isSingleHanded);
                    }
                } catch(Exception ex) {
                    Trace.TraceError("Failed to parse {0} due to exception:\n{1}", itemStr, ex.ToString());
                }
            }

            PrintToTrace();
        }

        public void PrintToTrace() {
            Trace.TraceInformation("Favorites List Phrases:");
            foreach (KeyValuePair<Grammar, string> entry in commandsByGrammar) {
                Trace.TraceInformation("Phrase '{0}' mapped to equip command '{1}'", entry.Key.Name, entry.Value);
            }
        }

        private bool hasPrefixOrSuffix(string text, string[] prefixOrSuffixArray) {
            //
            // NOTICE: Some languages (such as Chinese) don't add spaces between words.
            // So the code such as `result.Text.Split(' ').Last()` will not work for them.
            // Be aware of this when changing the code below.
            //
            foreach (string prefixOrSuffix in prefixOrSuffixArray) {
                if (useEquipHandPrefix ? text.StartsWith(prefixOrSuffix) : text.EndsWith(prefixOrSuffix)) {
                    return true;
                }
            }
            return false;
        }

        public string GetCommandForResult(RecognitionResult result) {
            Grammar grammar = result.Grammar;
            if (commandsByGrammar.ContainsKey(grammar)) {
                string command = commandsByGrammar[grammar];

                // Determine handedness
                //
                // NOTICE: Some languages (such as Chinese) don't add spaces between words.
                // So the code such as `result.Text.Split(' ').Last()` will not work for them.
                // Be aware of this when changing the code below.
                //
                if (hasPrefixOrSuffix(result.Text, bothHandsSuffix)) {
                    command += "0";
                } else if(hasPrefixOrSuffix(result.Text, rightHandSuffix)) {
                    command += "1";
                } else if (hasPrefixOrSuffix(result.Text, leftHandSuffix)) {
                    command += "2";
                } else {
                    // The user didn't ask for a specific hand, supply a default
                    command += mainHandId;
                }

                return command;
            }

            return null;
        }

        public List<Grammar> GetGrammars() {
            return new List<Grammar>(commandsByGrammar.Keys);
        }
    }
}
