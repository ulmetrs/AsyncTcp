using AsyncTcp;
using AsyncTest;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ClientApp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            /*
            var scenarios = new ClientScenarios(IPAddress.Loopback);
            //await scenarios.RunKeepAliveScenarioAsync(20).ConfigureAwait(false);
            await scenarios.RunDataScenarioAsync(1).ConfigureAwait(false);
            await Console.In.ReadLineAsync().ConfigureAwait(false);
            */

            var hostInfo = Dns.GetHostEntry(IPAddress.Loopback);
            var address = hostInfo.AddressList.FirstOrDefault(_ => _.AddressFamily == AddressFamily.InterNetwork);
            var endpoint = new IPEndPoint(address, 9050);
            var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            Console.WriteLine("Start process socket...");

            await ProcessSocket(socket, endpoint).ConfigureAwait(false);
        }

        static async Task ProcessSocket(Socket socket, IPEndPoint endpoint)
        {
            var waypoint = new Waypoint() { MatchIndex = 3, WaypointIndex = 15, FlipY = true, X = 1.5f, Y = 1.6f };
            var buffer = new byte[Marshal.SizeOf(waypoint)];
            MemoryMarshal.Write(new Span<byte>(buffer), ref waypoint);
            while (true)
            {
                await socket
                    .SendToAsync(buffer, SocketFlags.None, endpoint)
                    .ConfigureAwait(false);
            }
        }
    }
}