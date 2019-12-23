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
            // Using single quotes with Chinese may cause exceptions
            // Like this: "吉'扎格的火焰风暴卷轴"
            phrase = Regex.Replace(phrase, @"(?<![a-zA-Z])'", " ");
            return phrase.Trim();
        }
    }
}
