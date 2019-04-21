using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DSN {
    class Phrases {
        public static string normalize(string phrase) {
            // Double quotes are not allowed in the speech recognition engine
            phrase = phrase.Replace('"', ' ');
            phrase = Regex.Replace(phrase, @"\s+", " ");
            return phrase.Trim();
        }
    }
}
