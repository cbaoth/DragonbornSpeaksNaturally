using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DSN {
    class SkyrimInterop {

        private Configuration config = null;
        private ConsoleInput consoleInput = null;

        private System.Object dialogueLock = new System.Object();
        private DialogueList currentDialogue = null;
        private FavoritesList favoritesList = null;
        private ISpeechRecognitionManager recognizer;
        private Thread submissionThread;
        private Thread listenThread;
        private BlockingCollection<string> commandQueue;

        private bool dialogueEnabled;

        public SkyrimInterop(Configuration config, ConsoleInput consoleInput) {
            this.config = config;
            this.consoleInput = consoleInput;
            this.dialogueEnabled = (config.Get("Dialogue", "enabled", "1") == "1");
        }

        public void Start() {
            try {
                favoritesList = new FavoritesList(config);
                commandQueue = new BlockingCollection<string>();

                switch (config.GetRecognitionEngine()) {
                    case RecognitionEngine.Voice2Json:
                        recognizer = new Voice2JsonSpeechRecognition(config);
                        break;
                    case RecognitionEngine.Microsoft:
                    default:
                        recognizer = new MicrosoftSpeechRecognition(config);
                        break;
                }
                recognizer.OnDialogueLineRecognized += Recognizer_OnDialogueLineRecognized;

                // Start in command-mode
                recognizer.StartSpeechRecognition(false, config.GetConsoleCommandList(), favoritesList);

                listenThread = new Thread(ListenForInput);
                submissionThread = new Thread(SubmitCommands);
                submissionThread.Start();
                listenThread.Start();
            }
            catch (Exception ex) {
                Trace.TraceError("Failed to initialize speech recognition due to error:");
                Trace.TraceError(ex.ToString());
            }
        }

        public void Join() {
            listenThread.Join();
        }

        public void Stop() {
            config.Stop();

            // Notify threads to exit
            consoleInput.WriteLine(null);
            commandQueue.Add(null);

            recognizer.Stop();
        }

        public void SubmitCommand(string command) {
            commandQueue.Add(sanitize(command));
        }

        private static string sanitize(string command) {
            command = command.Trim();
            return command.Replace("\r", "");
        }

        private void SubmitCommands() {
            while(true) {
                string command = commandQueue.Take();

                // Thread exit signal
                if (command == null) {
                    config.Stop();
                    break;
                }

                Trace.TraceInformation("Sending command: {0}", command);
                Console.Write(command+"\n");
            }
        }

        private void ListenForInput() {
            try {
                // try to restore saved state after reloading the configuration file.
                consoleInput.RestoreSavedState();

                while (true) {
                    string input = consoleInput.ReadLine();

                    // input will be null when Skyrim terminated (stdin closed)
                    if (input == null) {
                        config.Stop();
                        break;
                    }

                    Trace.TraceInformation("Received command: {0}", input);
                    lock (dialogueLock) {
                        string[] tokens = input.Split('|');
                        string command = tokens[0];
                        if (command.Equals("START_DIALOGUE")) {
                            consoleInput.currentDialogue = input;
                            if (dialogueEnabled) {
                                currentDialogue = DialogueList.Parse(string.Join("|", tokens, 1, tokens.Length - 1), config);
                                // Switch to dialogue mode
                                recognizer.StartSpeechRecognition(true, currentDialogue);
                            } else {
                                Trace.TraceInformation("Dialogue was disabled, pause the speech recognition");
                                recognizer.StopRecognition();
                            }
                        } else if (command.Equals("STOP_DIALOGUE")) {
                            // Switch to command mode
                            recognizer.StartSpeechRecognition(false, config.GetConsoleCommandList(), favoritesList);
                            currentDialogue = null;
                            consoleInput.currentDialogue = null;
                        } else if (command.Equals("FAVORITES")) {
                            consoleInput.currentFavoritesList = input;
                            favoritesList.Update(string.Join("|", tokens, 1, tokens.Length - 1));
                            if(consoleInput.currentDialogue == null) {
                                recognizer.StartSpeechRecognition(false, config.GetConsoleCommandList(), favoritesList);
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Trace.TraceError(ex.ToString());
            }
        }

        private void Recognizer_OnDialogueLineRecognized(string text, Grammar grammar, string semantics) {
            lock (dialogueLock) {
                if (currentDialogue != null) {
                    int idx = currentDialogue.GetLineIndex(grammar);
                    if (idx != -1) {
                        SubmitCommand("DIALOGUE|" + currentDialogue.id + "|" + idx);
                    }
                } else {
                    string command = favoritesList.GetCommandForResult(text, grammar);
                    if(command != null) {
                        SubmitCommand("EQUIP|" + command);
                    } else {
                        command = config.GetConsoleCommandList().GetCommandForPhrase(grammar);
                        if (command != null) {
                            // Starting with @ is the SRGS file, the command is in semantics
                            if (command[0] == '@') {
                                command = semantics;
                            }
                            SubmitCommand("COMMAND|" + command);
                        }
                    }
                }
            }
        }
    }
}
