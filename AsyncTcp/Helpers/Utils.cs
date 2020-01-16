using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public static class Utils
    {
        public static async Task<IPAddress> GetIPAddress()
        {
            var ipHostInfo = await Dns.GetHostEntryAsync(Dns.GetHostName()).ConfigureAwait(false);
            // Try to return the first IPv4 Address
            foreach (var address in ipHostInfo.AddressList)
            {
                // Break on first IPv4 address.
                // InterNetworkV6 for IPv6
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return address;
                }
            }
            // Else return the first address
            if (ipHostInfo.AddressList.Length > 0)
            {
                return ipHostInfo.AddressList[0];
            }
            // Else return null
            return null;
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
    }
}