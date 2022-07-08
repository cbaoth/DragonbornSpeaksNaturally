using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DSN
{
    class Voice2JsonCli {
        public delegate void SpeechRecognizedHandler(string resultJson);
        public event SpeechRecognizedHandler SpeechRecognized;

        private Configuration config;

        private readonly Object ioLock = new Object();
        private long sessionId = 0;
        private Process process;
        private BinaryWriter stdIn;
        private Thread readStdOut;
        private Thread readStdErr;

        // voice2json expects 16-bit 16Khz mono audio as input
        private WaveInEvent mic = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };

        public Voice2JsonCli(Configuration config) {
            this.config = config;
            this.mic.DataAvailable += this.WaveSourceDataAvailable;

            init();
        }
        public void SetInputToDefaultAudioDevice() {
            mic.StopRecording();
            mic.StartRecording();
        }

        private void WaveSourceDataAvailable(object sender, WaveInEventArgs e) {
            //Trace.TraceInformation("WaveSourceDataAvailable: {0}", e.BytesRecorded);
            lock (ioLock) {
                if (stdIn != null && e.BytesRecorded > 0) {
                    stdIn.Write(e.Buffer);
                }
            }
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

                bool ok = false;
                try {
                    ok = process.Start();
                } catch {
                    // ignore
                }
                if (!ok) {
                    endSession();
                    throw new Exception("Run docker command failed, make sure you have Docker Desktop installed:\n" + command + " " + args);
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

        private void init() {
            int exitCode;
            if (runCommand("docker", "ps -a") != 0) {
                string dockerDesktopPath = @"C:\Program Files\Docker\Docker";
                try {
                    dockerDesktopPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Docker Inc.\Docker\1.0",
                        "AppPath", @"C:\Program Files\Docker\Docker").ToString();
                } catch {
                    // ignore
                }
                dockerDesktopPath += @"\Docker Desktop.exe";

                Trace.TraceInformation("Launch Docker Desktop");
                exitCode = runCommand(dockerDesktopPath, "", 5000, true);
                if (exitCode == 0) {
                    Trace.TraceInformation("Wait for Docker Desktop to be ready");
                    for (int i = 1; i < 20; i++) {
                        if (runCommand("docker", "ps -a") == 0) {
                            break;
                        } else {
                            Thread.Sleep(5000);
                        }
                    }
                } else if (exitCode != 2) {
                    Trace.TraceError("Failed to launch Docker Desktop, please make sure Docker Desktop is installed on your system.", exitCode);
                }
            }

            Trace.TraceInformation("Launch the docker container 'dsn_voice2json'");
            exitCode = runCommand("docker", "start dsn_voice2json");

            if (exitCode != 0) {
                Trace.TraceInformation("Create the docker container 'dsn_voice2json'");
                exitCode = runCommand("docker", "run -dit --name dsn_voice2json --entrypoint /bin/sh synesthesiam/voice2json");

                if (exitCode != 0) {
                    throw new Exception("The Voice2Json container cannot be created automatically, " +
                        "try creating it manually with the following command:\n" +
                        "docker run -dit --name dsn_voice2json --entrypoint /bin/sh synesthesiam/voice2json");
                }
            }

            Trace.TraceInformation("Automatically download speech recognition model files");
            runCommand("docker", "exec dsn_voice2json sh -c \"" + 
                "/usr/lib/voice2json/bin/voice2json -p " + config.GetLocale() + " train-profile; " +
                "ls /root/.local/share/voice2json | while read d; " +
                "do ln -sf /root/.local/share/voice2json/sentences.ini /root/.local/share/voice2json/$d/sentences.ini; done" +
                "\"");
        }

        public void LoadJSGF(string jsgf) {
            lock (ioLock) {
                process = new Process();
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = "exec -i dsn_voice2json sh -c \"cat > /root/.local/share/voice2json/sentences.ini; " +
                    "sed -i 's/\\uFEFF//g' /root/.local/share/voice2json/sentences.ini; " +
                    "/usr/lib/voice2json/bin/voice2json -p " + config.GetLocale() + " train-profile\"";

                if (!process.Start()) {
                    endSession();
                    throw new Exception("Run docker command failed, make sure you have Docker Desktop installed.");
                }

                readStdOut = new Thread(ReadStdOut);
                readStdErr = new Thread(ReadStdErr);
                readStdOut.Start();
                readStdErr.Start();

                var stdin = new BinaryWriter(process.StandardInput.BaseStream);
                stdin.Write(Encoding.UTF8.GetBytes(jsgf));
                stdin.Flush();
                stdin.Close();

                process.WaitForExit();
                var exitCode = process.ExitCode;
                endSession();

                if (exitCode != 0) {
                    throw new Exception("Unable to access docker container 'dsn_voice2json', please make sure your Docker Desktop is running.");
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
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = "exec -i dsn_voice2json sh -c \"/usr/lib/voice2json/bin/voice2json -p "
                    + config.GetLocale() + " transcribe-stream --audio-source -"
                    + " | /usr/lib/voice2json/bin/voice2json -p " + config.GetLocale() + " recognize-intent\"";

                if (!process.Start()) {
                    endSession();
                    throw new Exception("Run docker command failed, make sure you have Docker Desktop installed.");
                }

                stdIn = new BinaryWriter(process.StandardInput.BaseStream);
                readStdOut = new Thread(ReadRecognizeResult);
                readStdErr = new Thread(ReadStdErr);
                readStdOut.Start();
                readStdErr.Start();
            }
        }
    }
}
