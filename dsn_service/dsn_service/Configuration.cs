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
using System.Threading;
using System.Threading.Tasks;

namespace DSN {

    class Configuration {
        public static readonly string MY_DOCUMENT_DSN_DIR = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\DragonbornSpeaksNaturally\\";
        public static readonly string ERROR_LOG_FILE = "DragonbornSpeaksNaturally.log";

        private readonly string CONFIG_FILE_NAME = "DragonbornSpeaksNaturally.ini";
        private static readonly SubsetMatchingMode DEFAULT_GRAMMAR_MATCHING_MODE = SubsetMatchingMode.OrderedSubsetContentRequired;

        // NOTE: Relative to SkyrimVR.exe
        private readonly string[] SEARCH_DIRECTORIES = {
            "Data\\Plugins\\Sumwunn\\",
            "",
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

        private CultureInfo locale = CultureInfo.InstalledUICulture;

        public Configuration() {
            iniFilePath = resolveFilePath(CONFIG_FILE_NAME);

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
                Trace.TraceInformation("filepath: " + filepath);
                if (File.Exists(filepath)) {
                    return Path.GetFullPath(filepath); ;
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
