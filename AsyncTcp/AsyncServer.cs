using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncServer
    {
        private static readonly IPEndPoint _blankEndpoint = new IPEndPoint(IPAddress.Any, 0);

        private Socket _udpSocket;
        private Socket _tcpSocket;
        private readonly ConcurrentDictionary<IPAddress, AsyncPeer> _peers;
        private bool _alive;

        public string HostName { get; private set; }

        public AsyncServer()
        {
            if (!AsyncTcp.Initialized)
                throw new Exception("AsyncTcp must be initialized before creating a server");

            _peers = new ConcurrentDictionary<IPAddress, AsyncPeer>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start, server is running");

            if (address == null)
                throw new Exception("Specify server address to use");

            if (address.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("Address family must be of type Internetwork for UDP support");

            _udpSocket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _udpSocket.Bind(new IPEndPoint(IPAddress.Any, bindPort));
            _tcpSocket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _tcpSocket.Bind(new IPEndPoint(address, bindPort));
            _tcpSocket.Listen(100);

            HostName = address.ToString();

            _alive = true;

            var receiveTask = ProcessReceiveUnreliable();

            try
            {
                while (_alive)
                {
                    var socket = await _tcpSocket.AcceptAsync().ConfigureAwait(false);
                    socket.NoDelay = true;
                    _ = ProcessPeer(socket);
                }
            }
            catch { }

            ShutDown();

            await receiveTask.ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ProcessPeer(Socket socket)
        {
            var peer = new AsyncPeer(socket);
            _peers.TryAdd(peer.EndPoint.Address, peer);

            try
            {
                await peer.Process().ConfigureAwait(false);
            }
            catch { }

            _peers.TryRemove(peer.EndPoint.Address, out _);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ProcessReceiveUnreliable()
        {
            try
            {
                while (_alive)
                {
                    var result = await _udpSocket
                        .ReceiveFromAsync(new ArraySegment<byte>(AsyncTcp.UdpBuffer), SocketFlags.None, _blankEndpoint)
                        .ConfigureAwait(false);

                    var endpoint = (IPEndPoint)result.RemoteEndPoint;

                    if (_peers.TryGetValue(endpoint.Address, out var peer))
                    {
                        // Write the peer response endpoint:port
                        peer.UdpEndpoint = endpoint;

                        await AsyncTcp.PeerHandler
                            .ReceiveUnreliable(peer, new ReadOnlyMemory<byte>(AsyncTcp.UdpBuffer, 0, result.ReceivedBytes))
                            .ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task SendUnreliable(AsyncPeer peer, ReadOnlyMemory<byte> buffer)
        {
            try
            {
                await _udpSocket
                    .SendToAsync(buffer.GetArray(), SocketFlags.None, peer.UdpEndpoint)
                    .ConfigureAwait(false);
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(AsyncPeer peer, int type)
        {
            return peer.Send(type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(AsyncPeer peer, int type, ReadOnlyMemory<byte> buffer)
        {
            return peer.Send(type, buffer);
        }

        public void ShutDown()
        {
            _alive = false;

            try { _udpSocket.Shutdown(SocketShutdown.Both); } catch { }
            try { _udpSocket.Close(); } catch { }

            // If we never connect listener.Shutdown throws an error, so try separately
            try { _tcpSocket.Shutdown(SocketShutdown.Both); } catch { }
            try { _tcpSocket.Close(); } catch { }

            if (_peers.Count == 0)
                return;

            // Send Kill Signals to the Peer Sockets
            foreach (var kv in _peers)
            {
                kv.Value.ShutDown();
            }
        }

        public Task RemovePeer(AsyncPeer peer, ReadOnlyMemory<byte> buffer)
        {
            if (_peers.TryRemove(peer.EndPoint.Address, out _))
                return peer.Send(AsyncTcp.ErrorType, buffer);

            return Task.CompletedTask;
        }
    }
}