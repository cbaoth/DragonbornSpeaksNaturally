using IniParser.Model;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Xml;

namespace DSN {
    class CommandList : ISpeechRecognitionGrammarProvider {

        public static CommandList FromIniSection(IniData ini, string sectionName, Configuration config) {
            KeyDataCollection sectionData = ini.Sections[sectionName];
            CommandList list = new CommandList();
            if(sectionData != null) {
                foreach(KeyData key in sectionData) {
                    string value = key.Value.Trim();
                    if (value.Length == 0) {
                        Trace.TraceInformation("Ignore empty command '{0}'", key.KeyName);
                        continue;
                    }
                    RecognitionGrammar grammar = new RecognitionGrammar(config);
                    if (value[0] == '@') {
                        string path = config.ResolveFilePath(value.Substring(1));
                        if (path == null) {
                            Trace.TraceError("Cannot find the SRGS XML file '{0}', key: {1}", value.Substring(1), key.KeyName);
                            continue;
                        }
                        if (!grammar.FromSRGS(path)) {
                            continue;
                        }
                    } else {
                        Phrases.appendPhrase(grammar, Phrases.normalize(key.KeyName, config), config);
                    }
                    grammar.Name = key.KeyName;
                    list.commandsByPhrase[grammar] = value;
                }
            }
            return list;
        }

        public Dictionary<RecognitionGrammar, string> commandsByPhrase = new Dictionary<RecognitionGrammar, string>();

        public string GetCommandForPhrase(RecognitionGrammar grammar) {
            if (commandsByPhrase.ContainsKey(grammar))
                return commandsByPhrase[grammar];
            return null;
        }

        public void PrintToTrace() {
            Trace.TraceInformation("Command List Phrases:");
            foreach (KeyValuePair<RecognitionGrammar, string> entry in commandsByPhrase) {
                Trace.TraceInformation("Phrase '{0}' mapped to commands '{1}'", entry.Key.Name, entry.Value);
            }
        }

        public List<RecognitionGrammar> GetGrammars() {
            return commandsByPhrase.Keys.ToList();
        }
    }
}
