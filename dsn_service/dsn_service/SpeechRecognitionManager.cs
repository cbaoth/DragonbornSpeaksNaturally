using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Recognition;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NAudio.CoreAudioApi;

namespace DSN {
    class SpeechRecognitionManager : NAudio.CoreAudioApi.Interfaces.IMMNotificationClient {
        private const long STATUS_STOPPED = 0; // not in recognizing
        private const long STATUS_RECOGNIZING = 1; // in recognizing
        private const long STATUS_WAITING_DEVICE = 2; // waiting for record device

        private const string DEFAULT_PAUSE_AUDIO_FILE = @"C:\Windows\media\Speech Off.wav";
        private const string DEFAULT_RESUME_AUDIO_FILE = @"C:\Windows\media\Speech On.wav";

        public delegate void DialogueLineRecognitionHandler(RecognitionResult result);
        public event DialogueLineRecognitionHandler OnDialogueLineRecognized;

        private bool isPaused = false;
        private List<Grammar> pausePhrases = new List<Grammar>();
        private List<Grammar> resumePhrases = new List<Grammar>();
        private readonly string pauseAudioFile;
        private readonly string resumeAudioFile;

        private long recognitionStatus = STATUS_WAITING_DEVICE; // Need thread safety.
        private Thread waitingDeviceThread;

        private readonly SpeechRecognitionEngine DSN;
        private readonly Object dsnLock = new Object();

        // Dialogue can be more generous in the min confidence because
        // phrases are usually longer and more distinct amongst themselves
        private readonly float dialogueMinimumConfidence = 0.5f;
        private readonly float commandMinimumConfidence = 0.7f;
        private readonly bool logAudioSignalIssues = false;

        private bool isDialogueMode = false;
        private ISpeechRecognitionGrammarProvider[] grammarProviders;

        private string currentDeviceId = null;
        private readonly MMDeviceEnumerator deviceEnum = new MMDeviceEnumerator();
        private readonly Configuration config;

        public SpeechRecognitionManager(Configuration config) {
            this.config = config;

            string locale = config.Get("SpeechRecognition", "Locale", CultureInfo.InstalledUICulture.Name);
            dialogueMinimumConfidence = float.Parse(config.Get("SpeechRecognition", "dialogueMinConfidence", "0.5"), CultureInfo.InvariantCulture);
            commandMinimumConfidence = float.Parse(config.Get("SpeechRecognition", "commandMinConfidence", "0.7"), CultureInfo.InvariantCulture);
            logAudioSignalIssues = config.Get("SpeechRecognition", "bLogAudioSignalIssues", "0") == "1";

            Trace.TraceInformation("Locale: {0}\nDialogueConfidence: {1}\nCommandConfidence: {2}", locale, dialogueMinimumConfidence, commandMinimumConfidence);

            pauseAudioFile = config.Get("SpeechRecognition", "pauseAudioFile", DEFAULT_PAUSE_AUDIO_FILE).Trim();
            resumeAudioFile = config.Get("SpeechRecognition", "resumeAudioFile", DEFAULT_RESUME_AUDIO_FILE).Trim();

            List<string> pausePhraseStrings = config.GetPausePhrases();
            List<string> resumePhraseStrings = config.GetResumePhrases();
            foreach (string phrase in pausePhraseStrings) {
                if (phrase == null || phrase.Trim() == "")
                    continue;
                Trace.TraceInformation("Found pause phrase: '{0}'", phrase);
                try {
                    Grammar g = new Grammar(new GrammarBuilder(Phrases.normalize(phrase)));
                    pausePhrases.Add(g);
                } catch (Exception ex) {
                    Trace.TraceError("Failed to create grammar for pause phrase {0} due to exception:\n{1}", phrase, ex.ToString());
                }
            }
            foreach (string phrase in resumePhraseStrings) {
                if (phrase == null || phrase.Trim() == "")
                    continue;
                Trace.TraceInformation("Found resume phrase: '{0}'", phrase);
                try {
                    Grammar g = new Grammar(new GrammarBuilder(Phrases.normalize(phrase)));
                    resumePhrases.Add(g);
                } catch (Exception ex) {
                    Trace.TraceError("Failed to create grammar for resume phrase {0} due to exception:\n{1}", phrase, ex.ToString());
                }
            }

            this.DSN = new SpeechRecognitionEngine(new CultureInfo(locale));
            this.DSN.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 10); // Range is 0-100
            this.DSN.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(250);
            this.DSN.AudioStateChanged += DSN_AudioStateChanged;
            this.DSN.AudioSignalProblemOccurred += DSN_AudioSignalProblemOccurred;
            this.DSN.SpeechRecognized += DSN_SpeechRecognized;
            this.DSN.SpeechRecognitionRejected += DSN_SpeechRecognitionRejected;
            this.deviceEnum.RegisterEndpointNotificationCallback(this);

            WaitRecordingDeviceNonBlocking();
        }

        private void WaitRecordingDeviceNonBlocking() {
            // Waiting recording device in a new thread to avoid blocking
            waitingDeviceThread = new Thread(DoWaitRecordingDevice);
            waitingDeviceThread.Start();
        }

        private void DoWaitRecordingDevice() {
            lock (dsnLock) {
                StopRecognition();
                recognitionStatus = STATUS_WAITING_DEVICE;
            }

            // Select input device
            try {
                this.DSN.SetInputToDefaultAudioDevice();
                Trace.TraceInformation("Recording device is ready.");
            } catch {
                Trace.TraceInformation("Waiting for recording device...");
                while (config.IsRunning()) {
                    try {
                        this.DSN.SetInputToDefaultAudioDevice();
                        Trace.TraceInformation("Recording device is ready.");
                        break;
                    } catch {
                        Thread.Sleep(500);
                    }
                }
            }

            lock (dsnLock) {
                if (!config.IsRunning()) {
                    return;
                }

                MMDevice device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                if (device != null) {
                    currentDeviceId = device.ID;
                }

                recognitionStatus = STATUS_STOPPED;
                RestartRecognition();
                waitingDeviceThread = null;
            }
        }

        public void Stop() {
            config.Stop();
            StopRecognition();

            lock (dsnLock) {
                deviceEnum.UnregisterEndpointNotificationCallback(this);

                if (waitingDeviceThread != null) {
                    waitingDeviceThread.Abort();
                }
            }
        }

        private void RestartRecognition() {
            StartSpeechRecognition(isDialogueMode, grammarProviders);
        }

        private void DSN_AudioStateChanged(object sender, AudioStateChangedEventArgs e) {
            if (logAudioSignalIssues) {
                Trace.TraceInformation("Audio state changed: {0}", e.AudioState.ToString());
            }
        }

        private void DSN_AudioSignalProblemOccurred(object sender, AudioSignalProblemOccurredEventArgs e) {
            if (logAudioSignalIssues) {
                Trace.TraceInformation("Audio signal problem occurred during speech recognition: {0}", e.AudioSignalProblem.ToString());
            }
        }

        private void StopRecognition() {
            lock (dsnLock) {
                try {
                    if (recognitionStatus == STATUS_RECOGNIZING) {
                        this.DSN.RecognizeAsyncCancel();
                        recognitionStatus = STATUS_STOPPED;
                    }
                } catch (Exception e) {
                    Trace.TraceError("Failed to stop recognition due to exception");
                    Trace.TraceError(e.ToString());
                }
            }
        }

        public void StartSpeechRecognition(bool isDialogueMode, params ISpeechRecognitionGrammarProvider[] grammarProviders) {
            lock (dsnLock) {
                try {
                    this.isDialogueMode = isDialogueMode;
                    this.grammarProviders = grammarProviders;

                    if (recognitionStatus == STATUS_WAITING_DEVICE) {
                        // Avoid blocking and the program cannot quit when Skyrim is terminated
                        Trace.TraceInformation("Recording device is not ready");
                        return;
                    }

                    StopRecognition(); // Cancel previous recognition

                    List<Grammar> allGrammars = new List<Grammar>();
                    if (isPaused) {
                        allGrammars.AddRange(resumePhrases);
                    } else {
                        if (grammarProviders != null) {
                            allGrammars.AddRange(grammarProviders.SelectMany((x) => x.GetGrammars()).ToList());
                        }
                        allGrammars.AddRange(pausePhrases);
                    }
                    // Error is thrown if no grammars are loaded
                    if (allGrammars.Count > 0) {
                        SetGrammar(allGrammars);
                        this.DSN.RecognizeAsync(RecognizeMode.Multiple);
                        recognitionStatus = STATUS_RECOGNIZING;

                        Trace.TraceInformation(
                            "Recognition {0}: {1} mode, {2} phrases",
                            isPaused ? "paused" : "started",
                            isDialogueMode ? "dialogue" : "command",
                            allGrammars.Count
                        );
                    }
                } catch (Exception e) {
                    Trace.TraceError("Failed to start new phrase recognition due to exception");
                    Trace.TraceError(e.ToString());
                }
            }
        }

        private void SetGrammar(List<Grammar> grammars) {
            this.DSN.RequestRecognizerUpdate();
            this.DSN.UnloadAllGrammars();
            foreach (Grammar grammar in grammars) {
                try {
                    this.DSN.LoadGrammar(grammar);
                } catch (Exception ex) {
                    Trace.TraceError("Load grammar '{0}' failed:\n{1}", grammar.Name, ex.ToString());
                }
            }
        }

        private void DSN_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e) {
            // nothing to do
        }

        private void DSN_SpeechRecognized(object sender, SpeechRecognizedEventArgs e) {
            lock (dsnLock) {
                if (pausePhrases.Contains(e.Result.Grammar) || resumePhrases.Contains(e.Result.Grammar)) {
                    if (e.Result.Confidence >= commandMinimumConfidence) {
                        StopRecognition();

                        isPaused = !isPaused;
                        Trace.TraceInformation("****** Recognition {0} ******", isPaused ? "Paused" : "Resumed");

                        // Play a tone for notification
                        string file = isPaused ? pauseAudioFile : resumeAudioFile;
                        if (file.Count() != 0) {
                            try {
                                new System.Media.SoundPlayer(file).Play();
                            } catch (Exception ex) {
                                Trace.TraceError("Play {0} failed with exception:\n{1}", file, ex.ToString());
                            }
                        }

                        RestartRecognition();
                    } else {
                        Trace.TraceInformation("Recognized phrase '{0}' but ignored because confidence was too low (Confidence: {1})", e.Result.Text, e.Result.Confidence);
                    }
                    return;
                }

                float minConfidence = isDialogueMode ? dialogueMinimumConfidence : commandMinimumConfidence;
                if (e.Result.Confidence >= minConfidence) {
                    Trace.TraceInformation("Recognized phrase '{0}' (Confidence: {1})", e.Result.Text, e.Result.Confidence);
                    OnDialogueLineRecognized?.Invoke(e.Result);
                } else {
                    Trace.TraceInformation("Recognized phrase '{0}' but ignored because confidence was too low (Confidence: {1})", e.Result.Text, e.Result.Confidence);
                }
            }
        }

        public void OnDefaultDeviceChangedThread(DataFlow dataFlow, Role deviceRole, string defaultDeviceId) {
            lock (dsnLock) {
                if (defaultDeviceId == null && currentDeviceId != null) {
                    Trace.TraceInformation("****** Default audio device not available ******");
                    currentDeviceId = null;
                    return;
                }
                if (defaultDeviceId != currentDeviceId) {
                    Trace.TraceInformation("****** Default audio device changed ******");
                    currentDeviceId = defaultDeviceId;
                    WaitRecordingDeviceNonBlocking();
                }
            }
        }

        // IMMNotificationClient events
        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId) {
            if (dataFlow != DataFlow.Capture || deviceRole != Role.Console) {
                return;
            }
            // A deadlock will occur if you call `lock (dsnLock)` in this thread.
            new Thread(() => OnDefaultDeviceChangedThread(dataFlow, deviceRole, defaultDeviceId)).Start();
        }
        public void OnDeviceAdded(string deviceId) {
        }
        public void OnDeviceRemoved(string deviceId) {
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) {
        }
        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) {
        }
    }
}
