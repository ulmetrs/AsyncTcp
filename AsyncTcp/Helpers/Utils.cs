using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public static class Utils
    {
        public static Task ParallelForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> funcBody, int maxDoP = 4)
        {
            async Task AwaitPartition(IEnumerator<T> partition)
            {
                using (partition)
                {
                    while (partition.MoveNext())
                    { await funcBody(partition.Current).ConfigureAwait(false); }
                }
            }

            return Task.WhenAll(
                Partitioner
                    .Create(source)
                    .GetPartitions(maxDoP)
                    .AsParallel()
                    .Select(p => AwaitPartition(p)));
        }

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

        public static async Task<byte[]> CompressWithGzipAsync(byte[] input)
        {
            byte[] output = null;
            using (var compressedStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress))
                {
                    await gzipStream.WriteAsync(input, 0, input.Length).ConfigureAwait(false);
                }
                output = compressedStream.ToArray();
            }
            return output;
        }

        public static async Task<byte[]> DecompressWithGzipAsync(byte[] input)
        {
            using (var outputStream = new MemoryStream())
            {
                using (var compressedStream = new MemoryStream(input))
                using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress, false))
                {
                    await gzipStream.CopyToAsync(outputStream).ConfigureAwait(false);
                }
                return outputStream.ToArray();
            }
        }
    }
}
