using IniParser.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Speech.Recognition.SrgsGrammar;
using System.Text;
using System.Threading.Tasks;
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
                    Grammar grammar;
                    if (value[0] == '@') {
                        string path = config.ResolveFilePath(value.Substring(1));
                        if (path == null) {
                            Trace.TraceError("Cannot find the SRGS XML file '{0}', key: {1}", value.Substring(1), key.KeyName);
                            continue;
                        }

                        // load a SRGS XML file
                        XmlDocument doc = new XmlDocument();
                        doc.Load(path);

                        // If xml:lang in the file does not match the DSN's locale, the grammar cannot be loaded.
                        XmlAttribute xmlLang = doc.CreateAttribute("xml:lang");
                        xmlLang.Value = config.GetLocale().Name;
                        doc.DocumentElement.SetAttributeNode(xmlLang);

                        MemoryStream xmlStream = new MemoryStream();
                        doc.Save(xmlStream);
                        xmlStream.Flush(); //Adjust this if you want read your data 
                        xmlStream.Position = 0;

                        grammar = new Grammar(xmlStream);
                    } else {
                        grammar = Phrases.createGrammar(Phrases.normalize(key.KeyName, config), config);
                    }
                    grammar.Name = key.KeyName;
                    list.commandsByPhrase[grammar] = value;
                }
            }
            return list;
        }

        public Dictionary<Grammar, string> commandsByPhrase = new Dictionary<Grammar, string>();

        public string GetCommandForPhrase(Grammar grammar) {
            if (commandsByPhrase.ContainsKey(grammar))
                return commandsByPhrase[grammar];
            return null;
        }

        public void PrintToTrace() {
            Trace.TraceInformation("Command List Phrases:");
            foreach (KeyValuePair<Grammar, string> entry in commandsByPhrase) {
                Trace.TraceInformation("Phrase '{0}' mapped to commands '{1}'", entry.Key.Name, entry.Value);
            }
        }

        public List<Grammar> GetGrammars() {
            return commandsByPhrase.Keys.ToList();
        }
    }
}
