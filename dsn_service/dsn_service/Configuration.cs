using IniParser;
using IniParser.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DSN {

    class Configuration {
        public static readonly string WORKING_DIR = Directory.GetCurrentDirectory();
        public static readonly string MY_DOCUMENT_DSN_DIR = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\DragonbornSpeaksNaturally\\";
        public static readonly string ERROR_LOG_FILE = "DragonbornSpeaksNaturally.log";

        private static readonly string[] CONFIG_FILE_NAMES = {
            "DragonbornSpeaksNaturally.ini",
            // Relieve users' trouble of renaming if the extension ".ini" hidden
            "DragonbornSpeaksNaturally.ini.ini"
        };
        
        private static readonly SubsetMatchingMode DEFAULT_GRAMMAR_MATCHING_MODE = SubsetMatchingMode.OrderedSubsetContentRequired;

        // Double quotes are not allowed in the speech recognition engine.
        // About "(?<![a-zA-Z])'":
        //   Using single quotes with Chinese may cause exceptions.
        //   Like this: "吉'扎格的火焰风暴卷轴".
        //   So we need to remove the quote if it is not preceded by a letter.
        private static readonly string DEFAULT_NORMALIZE_EXPRESSION = @"(?:""|\s+|(?<![a-zA-Z])')";
        private static readonly string DEFAULT_NORMALIZE_REPLACEMENT = @" ";

        private static readonly string DEFAULT_OPTIONAL_EXPRESSION = @"(?:\(([^)]*)\)|\[([^\]]*)\]|{([^}]*)}|<([^>]*)>)";
        private static readonly string DEFAULT_OPTIONAL_REPLACEMENT = @"$1$2$3$4";

        // NOTE: Relative to SkyrimVR.exe
        private readonly string[] SEARCH_DIRECTORIES = {
            WORKING_DIR + "\\Data\\Plugins\\Sumwunn\\",
            WORKING_DIR + "\\",
            MY_DOCUMENT_DSN_DIR
        };

        // Can only be changed from 1 to 0,
        // used to coordinate other threads to stop.
        private int running = 1;

        private string iniFilePath = null;

        private IniData global = null;
        private IniData local = null;
        private IniData merged = null;

        private List<string> goodbyePhrases = null;
        private List<string> pausePhrases = null;
        private List<string> resumePhrases = null;
        private CommandList consoleCommandList = null;
        private bool enableDialogueSubsetMatching = true;
        private SubsetMatchingMode configuredMatchingMode = DEFAULT_GRAMMAR_MATCHING_MODE;

        private Regex normalizeExpression = null;
        private string normalizeReplacement = "";
        private Regex optionalExpression = null;
        private string optionalReplacement = "";

        private CultureInfo locale = CultureInfo.InstalledUICulture;

        public Configuration() {
            iniFilePath = resolveOneOfFilePath(CONFIG_FILE_NAMES);

            loadLocal();
            loadGlobal();

            merged = new IniData();
            merged.Merge(global);
            merged.Merge(local);

            string matchingMode = Get("Dialogue", "SubsetMatchingMode", Enum.GetName(typeof(SubsetMatchingMode), DEFAULT_GRAMMAR_MATCHING_MODE));
            try {
                if (matchingMode.ToLower() == "none") {
                    enableDialogueSubsetMatching = false;
                    Trace.TraceInformation("Dialogue SubsetMatchingMode Disabled");
                } else {
                    configuredMatchingMode = (SubsetMatchingMode)Enum.Parse(typeof(SubsetMatchingMode), matchingMode, true);
                    Trace.TraceInformation("Set Dialogue SubsetMatchingMode to {0}", configuredMatchingMode);
                }
            } catch (Exception ex) {
                Trace.TraceError("Failed to parse SubsetMatchingMode from ini file, falling back to default");
                Trace.TraceError(ex.ToString());
                configuredMatchingMode = DEFAULT_GRAMMAR_MATCHING_MODE;
            }

            goodbyePhrases = getPhrases("Dialogue", "goodbyePhrases");
            pausePhrases = getPhrases("SpeechRecognition", "pausePhrases");
            resumePhrases = getPhrases("SpeechRecognition", "resumePhrases");

            string nmlExpStr = Get("SpeechRecognition", "normalizeExpression", DEFAULT_NORMALIZE_EXPRESSION);
            if (nmlExpStr.Length > 0) {
                normalizeExpression = new Regex(nmlExpStr);
                normalizeReplacement = Get("SpeechRecognition", "normalizeReplacement", DEFAULT_NORMALIZE_REPLACEMENT);
            }

            string opExpStr = Get("SpeechRecognition", "optionalExpression", DEFAULT_OPTIONAL_EXPRESSION);
            if (opExpStr.Length > 0) {
                optionalExpression = new Regex(opExpStr);
                optionalReplacement = Get("SpeechRecognition", "optionalReplacement", DEFAULT_OPTIONAL_REPLACEMENT);
            }

            string localeStr = Get("SpeechRecognition", "Locale", "");
            if (localeStr.Length > 0) {
                locale = new CultureInfo(localeStr);
            }

            consoleCommandList = CommandList.FromIniSection(merged, "ConsoleCommands", this);
            consoleCommandList.PrintToTrace();
        }

        public void Stop() {
            Interlocked.Exchange(ref running, 0);
        }

        public bool IsRunning() {
            return Interlocked.CompareExchange(ref running, 1, 1) == 1;
        }

        public string GetIniFilePath() {
            return iniFilePath;
        }

        public string Get(string section, string key, string def) {
            string val = merged[section][key];
            if (val == null)
                return def;
            // Remove surrounding quotes
            if (val.Length >=2 && val.Substring(0, 1) == "\"" && val.Substring(val.Length - 1, 1) == "\"") {
                val = val.Substring(1, val.Length - 2);
            }
            return val;
        }

        private List<string> getPhrases(string section, string key) {
            List<string> phrases = new List<string>();
            string phrasesStr = merged[section][key];
            if (phrasesStr != null) {
                phrases.AddRange(phrasesStr.Split(';'));
                phrases.RemoveAll((str) => str == null || str.Trim() == "");
            }
            return phrases;
        }

        public List<string> GetGoodbyePhrases() {
            return goodbyePhrases;
        }

        public List<string> GetPausePhrases() {
            return pausePhrases;
        }

        public List<string> GetResumePhrases() {
            return resumePhrases;
        }

        public CommandList GetConsoleCommandList() {
            return consoleCommandList;
        }

        public bool IsSubsetMatchingEnabled() {
            return enableDialogueSubsetMatching;
        }

        public SubsetMatchingMode getConfiguredMatchingMode() {
            return configuredMatchingMode;
        }

        public Regex GetOptionalExpression() {
            return optionalExpression;
        }

        public string GetOptionalReplacement() {
            return optionalReplacement;
        }

        public Regex GetNormalizeExpression() {
            return normalizeExpression;
        }

        public string GetNormalizeReplacement() {
            return normalizeReplacement;
        }

        public CultureInfo GetLocale() {
            return locale;
        }

        private void loadGlobal() {
            global = new IniData();
        }

        private void loadLocal() {
            local = loadIniFromFilePath(iniFilePath);
            if (local == null)
                local = new IniData();
        }

        public string resolveFilePath(string filename) {
            foreach (string directory in SEARCH_DIRECTORIES) {
                string filepath = directory + filename;
                if (File.Exists(filepath)) {
                    Trace.TraceInformation("filepath found: " + filepath);
                    return Path.GetFullPath(filepath); ;
                }
                Trace.TraceInformation("filepath not found: " + filepath);
            }
            return null;
        }

        public string resolveOneOfFilePath(string[] filenames) {
            foreach (string filename in filenames) {
                string filepath = resolveFilePath(filename);
                if (filepath != null) {
                    return filepath;
                }
            }
            return null;
        }

        private IniData loadIniFromFilePath(string filepath) {
            if (filepath != null) {
                Trace.TraceInformation("Loading ini from path " + filepath);
                try {
                    var parser = new FileIniDataParser();
                    return parser.ReadFile(filepath);
                } catch (Exception ex) {
                    Trace.TraceError("Failed to load ini file at " + filepath);
                    Trace.TraceError(ex.ToString());
                }
            }
            return null;
        }
    }
}
