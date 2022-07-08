using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Speech.Recognition;
using System.Text.RegularExpressions;

namespace DSN {
    class DialogueList : ISpeechRecognitionGrammarProvider {

        private Configuration config;

        public long id { get; private set; }
        private Dictionary<RecognitionGrammar, int> grammarToIndex = new Dictionary<RecognitionGrammar, int>();

        public static DialogueList Parse(string input, Configuration config) {
            string[] tokens = input.Split('|');
            long id = long.Parse(tokens[0]);
            List<string> lines = new List<string>();
            for(int i = 1; i < tokens.Length; i++) {
                lines.Add(Phrases.normalize(tokens[i], config));
            }
            return new DialogueList(id, lines, config);
        }

        private DialogueList(long id, List<string> lines, Configuration config) {
            this.id = id;
            this.config = config;

            List<string> goodbyePhrases = config.GetGoodbyePhrases();

            for (int i = 0; i < lines.Count; i++) {
                string line = lines[i];
                if (line == null || line.Trim() == "")
                    continue;
                try {
                    RecognitionGrammar g = Phrases.createGrammar(line, config, config.IsSubsetMatchingEnabled());
                    g.Name = line;
                    grammarToIndex[g] = i;
                }
                catch(Exception ex) {
                    Trace.TraceError("Failed to create grammar for line {0} due to exception:\n{1}", line, ex.ToString());
                }
            }

            foreach(string phrase in goodbyePhrases) {
                if (phrase == null || phrase.Trim() == "")
                    continue;
                Trace.TraceInformation("Found goodbye phrase: '{0}'", phrase);
                try {
                    RecognitionGrammar g = Phrases.createGrammar(Phrases.normalize(phrase, config), config, config.IsSubsetMatchingEnabled());
                    g.Name = phrase;
                    grammarToIndex[g] = -2;
                } catch (Exception ex) {
                    Trace.TraceError("Failed to create grammar for exit dialogue phrase {0} due to exception:\n{1}", phrase, ex.ToString());
                }
            }
        }

        public int GetLineIndex(RecognitionGrammar grammar) {
            if (this.grammarToIndex.ContainsKey(grammar)) {
                return grammarToIndex[grammar];
            }
            return -1;
        }

        public List<RecognitionGrammar> GetGrammars() {
            return new List<RecognitionGrammar>(this.grammarToIndex.Keys);
        }

        public void PrintToTrace() {
            Trace.TraceInformation("Dialogue List:");
            foreach(RecognitionGrammar g in grammarToIndex.Keys) {
                Trace.TraceInformation("Line {0} : {1}", grammarToIndex[g], g.Name);
            }
        }
    }
}
