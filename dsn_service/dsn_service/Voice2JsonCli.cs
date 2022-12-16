using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DSN
{
    class Voice2JsonCli {
        private const int TRAIN_TIMEOUT = 120000; // 2min timout for voice2json training

        public delegate void SpeechRecognizedHandler(string resultJson);
        public event SpeechRecognizedHandler SpeechRecognized;

        private Configuration config;

        private readonly Object ioLock = new Object();
        private long sessionId = 0;
        private Process process;
        private StreamWriter stdIn;
        private Thread readStdOut;
        private Thread readStdErr;

        private WaveInEvent mic;

        public Voice2JsonCli(Configuration config) {
            this.config = config;
            init();
        }

        public void StopRecording() {
            if (mic != null) {
                mic.StopRecording();
                mic.Dispose();
                mic = null;
            }
        }

        public void SetInputToDefaultAudioDevice() {
            StopRecording();
            // voice2json expects 16-bit 16Khz mono audio as input
            mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
            this.mic.DataAvailable += this.WaveSourceDataAvailable;
            mic.StartRecording();
        }

        private void endSession() {
            Interlocked.Increment(ref sessionId);
            process = null;
        }

        private int runCommand(string command, string args = "", int waitMs = 0, bool elevating = false) {
             lock (ioLock) {
                process = new Process();
                process.StartInfo.FileName = command;
                process.StartInfo.Arguments = args;

                if (elevating) {
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.Verb = "runas";
                } else {
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                    process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                }

                Trace.TraceInformation("Executing: {0} {1}", command, args);
                bool ok = false;
                try {
                    ok = process.Start();
                } catch(Exception e) {
                    Trace.TraceError(e.Message);
                }
                if (!ok) {
                    endSession();
                    throw new Exception(String.Format("Voice2Json execution failed, please make sure that wsl (bash) is available and voice2json is installed correctly: {0} {1}",
                        command, args));
                }

                if (!elevating) {
                    readStdOut = new Thread(ReadStdOut);
                    readStdErr = new Thread(ReadStdErr);
                    readStdOut.Start();
                    readStdErr.Start();
                }

                int exitCode = 0;
                if (waitMs > 0) {
                    if (process.WaitForExit(waitMs))
                        exitCode = process.ExitCode;
                } else {
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
                endSession();
                return exitCode;
            }
        }

        private void init()
        {
            Trace.TraceInformation("Automatically downloading speech recognition model files");
            runCommand("bash",
                "-c \"voice2json -p " + config.GetLocale() + " train-profile\"",
                TRAIN_TIMEOUT);
            Trace.TraceInformation("Deploying sentences.ini");
            runCommand("bash", "-c \""
                + "find ~/.local/share/voice2json/ -maxdepth 1 -type d | while read d; do"
                + "  ln -sf ~/.dsn_sentences.ini \\${d}/sentences.ini; "
                + "done"
                + "\"",
                30000); // sec timeout
        }

        public void LoadJSGF(string jsgf) {
            lock (ioLock) {
                Trace.TraceInformation("Generating sentences.ini file and running training");
                process = new Process();
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.FileName = "bash";
                process.StartInfo.Arguments = "-c \""
                    + "base64 -id > ~/.dsn_sentences.ini; "
                    + "voice2json -p " + config.GetLocale() + " train-profile"
                    + "\"";

                Trace.TraceInformation("Executing: {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);

                if (!process.Start()) {
                    endSession();
                    throw new Exception(String.Format("Voice2Json execution failed, please make sure that bash (wsl) is available and voice2json is installed correctly: {0} {1}",
                        process.StartInfo.FileName, process.StartInfo.Arguments));
                }

                readStdOut = new Thread(ReadStdOut);
                readStdErr = new Thread(ReadStdErr);
                readStdOut.Start();
                readStdErr.Start();

                process.StandardInput.WriteLine(Base64Encode(Encoding.UTF8.GetBytes(jsgf)));
                process.StandardInput.Flush();
                process.StandardInput.Close();

                process.WaitForExit(TRAIN_TIMEOUT);
                var exitCode = process.ExitCode;
                endSession();

                if (exitCode != 0) {
                    throw new Exception(String.Format("Voice2Json execution failed, please make sure that bash (wsl) is available and voice2json is installed correctly: {0} {1}",
                        process.StartInfo.FileName, process.StartInfo.Arguments));
                }
            }
        }

        public void RecognizeAsyncCancel() {
            lock (ioLock) {
                if (process != null) {
                    stdIn = null;
                    process.Kill();
                    endSession();
                }
            }
        }

        private void ReadStdOut() {
            var mySessionId = Interlocked.Read(ref sessionId);
            var reader = process.StandardOutput;
            while (mySessionId == Interlocked.Read(ref sessionId)) {
                var line = reader.ReadLine();
                if (line == null) {
                    break;
                }
                Trace.Write("[I] " + line + "\n");
            }
        }

        private void ReadStdErr() {
            var mySessionId = Interlocked.Read(ref sessionId);
            var reader = process.StandardError;
            while (mySessionId == Interlocked.Read(ref sessionId)) {
                var line = reader.ReadLine();
                if (line == null) {
                    break;
                }
                Trace.Write("[E] " + line + "\n");
            }
        }

        private void ReadRecognizeResult() {
            var mySessionId = Interlocked.Read(ref sessionId);
            var reader = process.StandardOutput;
            while (mySessionId == Interlocked.Read(ref sessionId)) {
                var line = reader.ReadLine();
                if (line == null) {
                    break;
                }
                SpeechRecognized?.Invoke(line);
            }
        }

        public void RecognizeAsync() {
            lock (ioLock) {
                process = new Process();
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.FileName = "bash";
                process.StartInfo.Arguments = "-c \"base64 -id | voice2json -p "
                    + config.GetLocale() + " transcribe-stream --audio-source -"
                    + " | voice2json -p " + config.GetLocale() + " recognize-intent\"";
                Trace.TraceInformation("Executing: {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);

                if (!process.Start()) {
                    endSession();
                    throw new Exception(String.Format("Voice2Json execution failed, please make sure that bash (wsl) is available and voice2json is installed correctly: {0} {1}",
                        process.StartInfo.FileName, process.StartInfo.Arguments));
                }

                stdIn = process.StandardInput;
                readStdOut = new Thread(ReadRecognizeResult);
                readStdErr = new Thread(ReadStdErr);
                readStdOut.Start();
                readStdErr.Start();
            }
        }
        private void WaveSourceDataAvailable(object sender, WaveInEventArgs e) {
            //Trace.TraceInformation("WaveSourceDataAvailable: {0}, {1}", sessionId, e.BytesRecorded);
            lock (ioLock) {
                if (stdIn != null && e.BytesRecorded > 0) {
                    stdIn.WriteLine(Base64Encode(e.Buffer));
                }
            }
        }

        private string Base64Encode(byte[] buffer) {
            return System.Convert.ToBase64String(buffer);
        }
    }
}
