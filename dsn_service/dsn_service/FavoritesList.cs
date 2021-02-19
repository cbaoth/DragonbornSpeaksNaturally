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
        private bool useEquipHandInfix;
        private bool useEquipHandSuffix;

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

            enabled = config.Get("Favorites", "enabled", "0") == "1";
            useEquipHandPrefix = config.Get("Favorites", "useEquipHandPrefix", "1") == "1";
            useEquipHandInfix = config.Get("Favorites", "useEquipHandInfix", "1") == "1";
            useEquipHandSuffix = config.Get("Favorites", "useEquipHandSuffix", "1") == "1";

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

            leftHandSuffix = Phrases.normalize(config.Get("Favorites", "equipLeftSuffix", "off;left").Split(';'), config);
            rightHandSuffix = Phrases.normalize(config.Get("Favorites", "equipRightSuffix", "main;right").Split(';'), config);
            bothHandsSuffix = Phrases.normalize(config.Get("Favorites", "equipBothSuffix", "both").Split(';'), config);

            equipPhrasePrefix = Phrases.normalize(config.Get("Favorites", "equipPhrasePrefix", "equip;wear;use").Split(';'), config);

            omitHandSuffix = config.Get("Favorites", "omitHandSuffix", "0") == "1";

            mainHand = config.Get("Favorites", "mainHand", "right");

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
                // User does not specify the main hand. Equipped with right hand by default.
                mainHandId = "1";
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
            itemName = Phrases.normalize(itemName, config).ToLower();

            foreach (string type in knownEquipmentTypes) {
                if (itemName.Contains(type)) {
                    return type;
                }
            }
            return null;
        }

        public void BuildAndAddGrammar(string[] equipPrefix, string phrase, string command, bool isSingleHanded)
        {
            List<string> handsSuffix = new List<string>();
            handsSuffix.AddRange(bothHandsSuffix);
            handsSuffix.AddRange(rightHandSuffix);
            handsSuffix.AddRange(leftHandSuffix);
            Choices handChoice = new Choices(handsSuffix.ToArray());

            GrammarBuilder grammarBuilder = new GrammarBuilder();

            // Append hand choice prefix
            if (isSingleHanded && useEquipHandPrefix && handsSuffix.Count > 0)
            {
                // Optional left/right. When excluded, try to equip to both hands
                grammarBuilder.Append(handChoice, 0, 1);
            }

            if (equipPrefix.Length > 0) {
                Choices equipPrefixChoice = new Choices(equipPrefix.ToArray());
                grammarBuilder.Append(equipPrefixChoice, omitHandSuffix ? 0 : 1, 1);
            }

            // Append hand choice infix
            if (isSingleHanded && useEquipHandInfix && handsSuffix.Count > 0) {
                // Optional left/right. When excluded, try to equip to both hands
                grammarBuilder.Append(handChoice, 0, 1);
            }

            Phrases.appendPhrase(grammarBuilder, phrase, config);

            // Append hand choice suffix
            if (isSingleHanded && useEquipHandSuffix && handsSuffix.Count > 0)
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
            string filepath = config.GetItemNameMap();
            if(filepath != null)
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

                    BuildAndAddGrammar(equipPhrasePrefix, Phrases.normalize(itemName, config), command, isSingleHanded);

                    // Are we looking at an equipment of some sort?
                    // Record the first item of a specific weapon type
                    string equipmentType = ProbableEquipmentType(itemName);
                    if(equipmentType != null && !firstEquipmentOfType.ContainsKey(equipmentType))
                    {
                        Trace.TraceInformation("ProbableEquipmentType: {0} -> {1}", itemName, equipmentType);
                        BuildAndAddGrammar(equipPhrasePrefix, Phrases.normalize(equipmentType, config), command, isSingleHanded);
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

        private bool hasSuffix(string text, string[] suffixArray) {
            foreach (string suffix in suffixArray) {
                // NOTICE: Some languages (such as Chinese) don't add spaces between words.
                // So the code such as `result.Text.Split(' ').Last()` will not work for them.
                // Be aware of this when changing the code below.
                if (text.Contains(suffix)) {
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
                if (hasSuffix(result.Text, bothHandsSuffix)) {
                    command += "0";
                } else if(hasSuffix(result.Text, rightHandSuffix)) {
                    command += "1";
                } else if (hasSuffix(result.Text, leftHandSuffix)) {
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
