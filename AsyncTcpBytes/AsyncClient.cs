using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private static readonly IPEndPoint _blankEndpoint = new IPEndPoint(IPAddress.Any, 0); // This is just used in type validation on ReceiveFromAsync, this should match any type

        private IPEndPoint _server;
        private Socket _udpSocket;
        private AsyncPeer _peer;
        private bool _alive;

        public AsyncClient()
        {
            if (!AsyncTcp.Initialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");
        }

        public async Task Start(IPAddress address, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start, client is running");

            if (address == null)
                throw new Exception("Specify server address to use");

            _server = new IPEndPoint(address, bindPort);
            var tcpSocket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _udpSocket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            await tcpSocket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);

            _alive = true;

            var receiveTask = ProcessReceiveUDP();

            try
            {
                _peer = new AsyncPeer(tcpSocket);

                await _peer.Process().ConfigureAwait(false);
            }
            catch
            { }

            ShutDown();

            await receiveTask.ConfigureAwait(false);
        }

        public async Task ProcessReceiveUDP()
        {
            try
            {
                while (_alive)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(512);
                    var result = await _udpSocket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, _blankEndpoint).ConfigureAwait(false);
                    // On the client we synchronize the udp packets for you
                    await ProcessUDPBuffer(_peer, buffer).ConfigureAwait(false);
                }
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ProcessUDPBuffer(AsyncPeer peer, byte[] buffer)
        {
            try
            {
                var (type, size) = ReadHeader(buffer);

                if (size == 0)
                {
                    await AsyncTcp.PeerHandler.UnpackUnreliableMessage(peer, type, null).ConfigureAwait(false);
                    return;
                }

                using (var stream = new MemoryStream(size))
                {
                    await stream.WriteAsync(buffer, 8, size).ConfigureAwait(false);

                    stream.Position = 0;

                    await AsyncTcp.PeerHandler.UnpackUnreliableMessage(peer, type, stream).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (int, int) ReadHeader(byte[] buffer)
        {
            var typeSpan = new ReadOnlySpan<byte>(buffer, 0, 4);
            var sizeSpan = new ReadOnlySpan<byte>(buffer, 4, 4);
            return (MemoryMarshal.Read<int>(typeSpan), MemoryMarshal.Read<int>(sizeSpan));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(int type, object payload = null)
        {
            return _peer.Send(type, payload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task SendUnreliable(int type, object payload)
        {
            if (payload == null)
            {
                try
                {
                    await _udpSocket
                        .SendToAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(type)), SocketFlags.None, _peer.EndPoint)
                        .ConfigureAwait(false);
                }
                catch { }
                return;
            }

            using (var stream = AsyncTcp.StreamManager.GetStream())
            {
                // Skip the header and pack the stream
                stream.Position = 8;

                await AsyncTcp.PeerHandler.PackUnreliableMessage(_peer, type, payload, stream).ConfigureAwait(false);

                // Go back and write the header now that we know the size
                stream.Position = 0;
                WriteHeader(stream, type, (int)stream.Length - 8);

                stream.Position = 0;
                if (stream.TryGetBuffer(out var buffer))
                {
                    try
                    {
                        await _udpSocket
                            .SendToAsync(new ArraySegment<byte>(buffer.Array, buffer.Offset, buffer.Count), SocketFlags.None, _peer.EndPoint)
                            .ConfigureAwait(false);
                    }
                    catch { }
                }
            }

            await AsyncTcp.PeerHandler.DisposeMessage(_peer, type, payload).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteHeader(Stream stream, int type, int size)
        {
            Span<byte> buffer = stackalloc byte[4];
            MemoryMarshal.Write(buffer, ref type);
            stream.Write(buffer);
            MemoryMarshal.Write(buffer, ref size);
            stream.Write(buffer);
        }

        public void ShutDown()
        {
            _peer.ShutDown();
            try { _udpSocket.Dispose(); } catch { }
        }
    }
}