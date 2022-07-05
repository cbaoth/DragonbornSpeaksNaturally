using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DSN {
    class Phrases {
        public static string normalize(string phrase, Configuration config) {
            var regex = config.GetNormalizeExpression();
            var repl = config.GetNormalizeReplacement();
            if (regex != null) {
                phrase = regex.Replace(phrase, repl);
            }
            return phrase.Trim();
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

        public static void appendPhrase(GrammarBuilder builder, string phrase, Configuration config, bool isSubsetMatchingEnabled = false) {
            var optionalExpression = config.GetOptionalExpression();
            var optionalReplacement = "\0" + config.GetOptionalReplacement() + "\0";
            SubsetMatchingMode matchingMode = config.GetConfiguredMatchingMode();

            if (optionalExpression == null) {
                if (isSubsetMatchingEnabled) {
                    builder.Append(phrase, matchingMode);
                } else {
                    builder.Append(phrase);
                }
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
                    if (isSubsetMatchingEnabled) {
                        builder.Append(part, matchingMode);
                    } else {
                        builder.Append(part);
                    }
                } else {
                    builder.Append(part, 0, 1);
                }
            }
        }

        public static Grammar createGrammar(string phrase, Configuration config, bool isSubsetMatchingEnabled = false) {
            GrammarBuilder builder = new GrammarBuilder();
            appendPhrase(builder, phrase, config, isSubsetMatchingEnabled);
            return new Grammar(builder);
        }
    }
}
