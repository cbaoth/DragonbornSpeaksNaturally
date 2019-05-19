using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DSN {
    class Log {
        public static void Initialize() {
            string logFilePath = Configuration.MY_DOCUMENT_DSN_DIR + Configuration.ERROR_LOG_FILE;
            try {
                // The compiler constant TRACE needs to be defined, otherwise logs will not be output to the file.
                var listener = new TextWriterTraceListener(logFilePath);
                listener.TraceOutputOptions = TraceOptions.DateTime;
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
            }
            catch(Exception ex) {
                Console.Error.WriteLine("Failed to create log file at " + logFilePath + ": {0}", ex.ToString());
            }
    
        }
    }
}
