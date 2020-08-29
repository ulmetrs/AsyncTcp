using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public static class Logging
    {
        // String Error Messages
        public const string SendErrorMessage = "Send Error: {0}";
        public const string ReceiveErrorMessage = "Received Error: {0}";
        public const string PeerConnectedErrorMessage = "Peer Connected Error: {0}";
        public const string PeerRemovedErrorMessage = "Peer Disconnected Error: {0}";

        // Tracers
        public const string SimpleTracerMessageFormatter = "{0}: {1}";
        public const string SimpleErrorTracerFormatter = "{0}:\nError: {1}\n\n{2}\n\n";
        public const string SimpleErrorTracerMessageFormatter = "{0}: {1}\n\nError: {2}\n\n{3}\n\n";

        // TODO: Connect Trace LogProvider to AWS CloudWatch (Setup app.config for Trace LogProvider)
        public static async Task LogMessageAsync(string message, bool printToConsole = false)
        {
            var logMessage = string.Format(SimpleTracerMessageFormatter, DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm:ss.fff"), message);
            Trace.WriteLine(logMessage);

            if (printToConsole)
            {
                await Console.Out.WriteLineAsync(logMessage).ConfigureAwait(false);
            }
        }

        public static void LogError(Exception ex, string message = null, bool printToConsole = false)
        {
            string logMessage;
            if (message == null)
            { logMessage = string.Format(SimpleErrorTracerFormatter, DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm:ss.fff"), ex.Message, ex.StackTrace); }
            else
            { logMessage = string.Format(SimpleErrorTracerMessageFormatter, DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm:ss.fff"), message, ex.Message, ex.StackTrace); }

            Trace.WriteLine(logMessage);

            if (printToConsole)
            {
                Console.WriteLine(logMessage);
            }
        }

        public static async Task LogErrorAsync(Exception ex, string message = null, bool printToConsole = false)
        {
            string logMessage;
            if (message == null)
            { logMessage = string.Format(SimpleErrorTracerFormatter, DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm:ss.fff"), ex.Message, ex.StackTrace); }
            else
            { logMessage = string.Format(SimpleErrorTracerMessageFormatter, DateTime.UtcNow.ToString("dd/MM/yyyy hh:mm:ss.fff"), message, ex.Message, ex.StackTrace); }

            Trace.WriteLine(logMessage);

            if (printToConsole)
            {
                await Console.Out.WriteLineAsync(logMessage).ConfigureAwait(false);
            }
        }
    }
}