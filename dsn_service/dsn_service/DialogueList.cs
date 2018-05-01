﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Speech.Recognition;
using System.Text.RegularExpressions;

namespace DSN {
    class DialogueList : ISpeechRecognitionGrammarProvider {

        private static readonly SubsetMatchingMode DEFAULT_GRAMMAR_MATCHING_MODE = SubsetMatchingMode.OrderedSubsetContentRequired;

        public static DialogueList Parse(string input) {
            string[] tokens = input.Split('|');
            long id = long.Parse(tokens[0]);
            List<string> lines = new List<string>();
            for(int i = 1; i < tokens.Length; i++) {
                lines.Add(Phrases.normalize(tokens[i]));
            }
            return new DialogueList(id, lines, Configuration.GetGoodbyePhrases());
        }

        public long id { get; private set; }
        private Dictionary<Grammar, int> grammarToIndex = new Dictionary<Grammar, int>();

        private DialogueList(long id, List<string> lines, List<string> goodbyePhrases) {
            this.id = id;
            SubsetMatchingMode matchingMode = getConfiguredMatchingMode();

            for(int i = 0; i < lines.Count; i++) {
                string line = lines[i];
                if (line == null || line.Trim() == "")
                    continue;
                Grammar g = new Grammar(new GrammarBuilder(line, matchingMode));
                grammarToIndex[g] = i;
            }

            foreach(string phrase in goodbyePhrases) {
                if (phrase == null || phrase.Trim() == "")
                    continue;
                Trace.TraceInformation("Found goodbye phrase: '{0}'", phrase);
                Grammar g = new Grammar(new GrammarBuilder(phrase, matchingMode));
                grammarToIndex[g] = -2;
            }
        }

        public int GetLineIndex(Grammar grammar) {
            if (this.grammarToIndex.ContainsKey(grammar)) {
                return grammarToIndex[grammar];
            }
            return -1;
        }

        public List<Grammar> GetGrammars() {
            return new List<Grammar>(this.grammarToIndex.Keys);
        }

        public void PrintToTrace() {
            Trace.TraceInformation("Dialogue List:");
            foreach(Grammar g in grammarToIndex.Keys) {
                Trace.TraceInformation("Line {0} : {1}", grammarToIndex[g], g.ToString());
            }
        }
        private SubsetMatchingMode getConfiguredMatchingMode() {

            string matchingMode = Configuration.Get("SpeechRecognition", "SubsetMatchingMode", Enum.GetName(typeof(SubsetMatchingMode), DEFAULT_GRAMMAR_MATCHING_MODE));

            try {
                return (SubsetMatchingMode)Enum.Parse(typeof(SubsetMatchingMode), matchingMode, true);
            } catch (Exception ex) {
                Trace.TraceError("Failed to parse SubsetMatchingMode from ini file, falling back to default");
                Trace.TraceError(ex.ToString());
                return DEFAULT_GRAMMAR_MATCHING_MODE;
            }
        }
    }
}
