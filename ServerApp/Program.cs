using AsyncTcp;
using AsyncTest;
using Microsoft.IO;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ServerApp
{
    public static class Program
    {
        private static RecyclableMemoryStreamManager _streams { get; set; } = new RecyclableMemoryStreamManager();
        private static readonly IPEndPoint _blankEndpoint = new IPEndPoint(IPAddress.Any, 0);

        public static async Task Main(string[] args)
        {
            /*
            var scenarios = new ServerScenarios();
            await scenarios.RunServer().ConfigureAwait(false);
            await Console.In.ReadLineAsync().ConfigureAwait(false);
            */

            var hostInfo = Dns.GetHostEntry(Dns.GetHostName());
            var address = hostInfo.AddressList.FirstOrDefault(_ => _.AddressFamily == AddressFamily.InterNetwork);
            var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 9050));

            Console.WriteLine("Start process socket...");

            await ProcessSocketShared(socket).ConfigureAwait(false);
        }

        // 9594
        // 9093
        // 9307
        // 9195
        static async Task ProcessSocket(Socket socket)
        {
            var buffer = new byte[512];

            await socket
                .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 1000000; i++)
            {
                // Get a recyclable stream, to prevent a stream copy we get underlying bytes pass to socket
                var stream = _streams.GetStream(null, 512);
                var result = await socket
                    .ReceiveFromAsync(stream.GetBuffer(), SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                stream.SetLength(result.ReceivedBytes);

                //Console.WriteLine("Process: " + i);

                _ = ProcessStream(stream);
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }

        // 8652
        // 8660
        // 8730
        // 8723
        static async Task ProcessSocket2(Socket socket)
        {
            var buffer = new byte[512];

            await socket
                .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 1000000; i++)
            {
                await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                // Get a recyclable stream copy of static buffer, I think we need to unblock socket as
                // fast as possible, getting a stream here
                var stream = _streams.GetStream(buffer) as RecyclableMemoryStream;

                //Console.WriteLine("Process: " + i);

                _ = ProcessStream(stream);
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }

        // 8617
        // 8644
        // 8781
        // 8556
        static async Task ProcessSocketRentedStream(Socket socket)
        {
            var buffer = new byte[512];

            await socket
                .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 1000000; i++)
            {
                buffer = ArrayPool<byte>.Shared.Rent(512);
                var result = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                //Console.WriteLine("Process: " + i);

                _ = ProcessBufferStream(buffer, result.ReceivedBytes);
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }

        // cant marshal string, delayed test
        static async Task ProcessSocketRentedMarshal(Socket socket)
        {
            var buffer = new byte[512];

            await socket
                .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 100000; i++)
            {
                buffer = ArrayPool<byte>.Shared.Rent(512);
                var result = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                Console.WriteLine("Process: " + i);

                _ = ProcessBufferMarshal(buffer, result.ReceivedBytes);
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }



        // This reads the object at time of receive, you add read time at receive,
        // but you also lower the number of tasks created/bytes outstanding, you just need to
        // return struct pool

        // 8783
        // 8832
        // 8794
        // 8896
        static async Task ProcessSocketShared(Socket socket)
        {
            var bytes = new byte[512];

            await socket
                .ReceiveFromAsync(bytes, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var stream = new MemoryStream(bytes);
            var reader = new BinaryReader(stream);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 1000000; i++)
            {
                var result = await socket
                    .ReceiveFromAsync(bytes, SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                stream.Position = 0;
                stream.SetLength(result.ReceivedBytes);

                var waypoint = default(Waypoint);
                waypoint.Read(reader);

                //Console.WriteLine("Process: " + i);

                //_ = ProcessWaypoint(waypoint);
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }

        // 9496
        // 9092
        // 9361
        // 9463
        static async Task ProcessSocketFinal(Socket socket)
        {
            var bytes = new byte[512];

            await socket
                .ReceiveFromAsync(bytes, SocketFlags.None, _blankEndpoint)
                .ConfigureAwait(false);

            var startTime = DateTime.UtcNow;
            for (int i = 0; i < 1000000; i++)
            {
                var result = await socket
                    .ReceiveFromAsync(bytes, SocketFlags.None, _blankEndpoint)
                    .ConfigureAwait(false);

                var waypoint = MemoryMarshal.Read<Waypoint>(new Span<byte>(bytes, 0, result.ReceivedBytes));
            }
            var endTime = DateTime.UtcNow;
            Console.WriteLine("Process Time: " + (endTime - startTime).TotalMilliseconds);
        }

        // This one uses a rented stream, creates/rents a new Waypoint and processes it
        // This is good since you can choose type later
        static async Task ProcessStream(Stream stream)
        {
            try
            {
                // Read and Process async
                var waypoint = default(Waypoint);
                waypoint.Read(new BinaryReader(stream));

                //await Task.Delay(1).ConfigureAwait(false);
            }
            finally
            {
                //Console.WriteLine("Disposing stream: " + stream.Length);
                stream.Dispose();
                // TODO return waypoint to pool
            }
        }

        // This one uses marshal method on rented bytes
        // Good that you can choose type later
        static async Task ProcessBufferStream(byte[] bytes, int size)
        {
            try
            {
                using (var stream = _streams.GetStream(bytes))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        var waypoint = new Waypoint();
                        waypoint.Read(reader);

                        //await Task.Delay(1).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
                // TODO return waypoint to pull
            }
        }

        // This one uses marshal method on rented bytes
        // Good that you can choose type later
        static async Task ProcessBufferMarshal(byte[] bytes, int size)
        {
            try
            {
                // Read and Process async
                var ptr = Marshal.AllocHGlobal(size);
                Marshal.Copy(bytes, 0, ptr, size);
                var s = (Waypoint)Marshal.PtrToStructure(ptr, typeof(Waypoint)); // This allocs a struct tho
                Marshal.FreeHGlobal(ptr);

                //await Task.Delay(1).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
                // TODO return waypoint to pull
            }
        }

        // This one the waypoint is pre-deserialized, so our await is just on handling
        static async Task ProcessWaypoint(Waypoint waypoint)
        {
            try
            {
                //await Task.Delay(1).ConfigureAwait(false);
            }
            finally
            {
                // TODO return waypoint to pool
            }
        }

        static async Task ProcessMemory(Memory<byte> buffer)
        {
            var waypoint = MemoryMarshal.Read<Waypoint>(buffer.Span);
        }

        static void Test()
        {
            var x = 0;
            var then = DateTime.Now;
            for (int i = 0; i < 100000; i++)
                x++;
            var now = DateTime.Now;
            Console.WriteLine("100k loop time: " + (now - then).TotalMilliseconds);
        }

        static void TestLoop()
        {
            var x = 0;
            var then = DateTime.Now;
            try
            {
                for (int i = 0; i < 10000000; i++)
                    x++;
            }
            catch { }
            var now = DateTime.Now;
            Console.WriteLine("10m loop time: " + (now - then).TotalMilliseconds);
        }

        static void TestTryCatch()
        {
            var x = 0;
            var then = DateTime.Now;
            for (int i = 0; i < 10000000; i++)
            {
                try
                {
                    x++;
                }
                catch { }
            }
                
            var now = DateTime.Now;
            Console.WriteLine("10m loop time: " + (now - then).TotalMilliseconds);
        }
    }
}