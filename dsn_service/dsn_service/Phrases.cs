using JiebaNet.Segmenter;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DSN {
    class Phrases {
        private static readonly string ZH_SEGMENTER_DICT_PATH = "Resources\\base_dictionary.txt";

        private static Regex blankRegex = new Regex("\\s+");
        private static Regex characterRegex = new Regex("\\p{IsCJKUnifiedIdeographs}");

        private static JiebaSegmenter segmenter = new JiebaSegmenter();
        private static HashSet<string> segmenterDict = null;

        public static string cleanBlank(string str) {
            return blankRegex.Replace(str, " ").Trim();
        }

        public static string normalize(string phrase, Configuration config) {
            var regex = config.GetNormalizeExpression();
            var repl = config.GetNormalizeReplacement();
            if (regex != null) {
                phrase = regex.Replace(phrase, repl);
            }

            // word segmentation for Chinese
            if (config.NeedSegmenter()) {
                phrase = wordCut(phrase);
            }

            return cleanBlank(phrase);
        }

        private static void loadSegmenterDict() {
            if (segmenterDict != null) {
                return;
            }
            segmenterDict = new HashSet<string>();
            var lines = File.ReadLines(ZH_SEGMENTER_DICT_PATH);
            foreach (var line in lines) {
                segmenterDict.Add(line.Split(new char[] { ' ', '\t', '('})[0]);
            }
        }

        // word segmentation for Chinese
        public static string wordCut(string phrase) {
            var list = segmenter.Cut(phrase).ToList();
            loadSegmenterDict();
            for (int i = 0; i < list.Count; i++) {
                // The word is not in the speech engine's dictionary.
                // Split it into individual characters.
                if (list[i].Length > 0 && !segmenterDict.Contains(list[i])) {
                    list[i] = characterRegex.Replace(list[i], " $0 ");
                }
            }
            return string.Join(" ", list);
        }

        public static string[] normalize(string[] phrases, Configuration config) {
            List<string> newPhrases = new List<string>();
            for (int i=0; i<phrases.Length; i++) {
                string phrase = normalize(phrases[i], config);
                if (phrase.Length > 0) {
                    newPhrases.Add(phrase);
                }
            }
            return newPhrases.ToArray();
        }

        public static void appendPhrase(RecognitionGrammar grammar, string phrase, Configuration config, bool isSubsetMatchingEnabled = false) {
            var optionalExpression = config.GetOptionalExpression();
            var optionalReplacement = "\0" + config.GetOptionalReplacement() + "\0";

            if (optionalExpression == null) {
                grammar.Append(phrase, false, isSubsetMatchingEnabled);
                return;
            }

            phrase = optionalExpression.Replace(phrase, optionalReplacement);
            var phraseParts = phrase.Split('\0');
            for (int i = 0; i < phraseParts.Length; i++) {
                string part = phraseParts[i];
                if (part.Trim().Length == 0) {
                    continue;
                }

                // Even numbers are non-optional and odd numbers are optional
                if (i % 2 == 0) {
                    grammar.Append(part, false, isSubsetMatchingEnabled);
                } else {
                    grammar.Append(part, true, isSubsetMatchingEnabled);
                }
            }
        }

        public static RecognitionGrammar createGrammar(string phrase, Configuration config, bool isSubsetMatchingEnabled = false) {
            RecognitionGrammar grammar = new RecognitionGrammar(config);
            appendPhrase(grammar, phrase, config, isSubsetMatchingEnabled);
            return grammar;
        }
    }
}
