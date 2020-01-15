using System;
using System.Diagnostics;
using System.Threading.Tasks;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public static class Logging
    {
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