using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcpBytes
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
            return ipHostInfo.AddressList.FirstOrDefault();
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
                    .Select(AwaitPartition));
        }
    }
}