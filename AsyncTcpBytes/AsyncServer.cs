using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcpBytes
{
    public class AsyncServer
    {
        private readonly ConcurrentDictionary<long, AsyncPeer> _peers;
        private Socket _listener;
        private bool _alive;

        public string HostName { get; private set; }

        public AsyncServer()
        {
            if (!AsyncTcp.IsConfigured)
                throw new Exception("AsyncTcp must be configured before creating a server");

            _peers = new ConcurrentDictionary<long, AsyncPeer>();
        }

        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start, server is running");

            if (address == null)
                address = await Utils.GetIPAddress().ConfigureAwait(false);

            _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _listener.Bind(new IPEndPoint(address, bindPort));
            _listener.Listen(100);

            _alive = true;

            HostName = address.ToString();

            Socket socket;
            try
            {
                // Accept all connections while server running
                while (true)
                {
                    socket = await _listener.AcceptAsync().ConfigureAwait(false);
                    socket.NoDelay = true;
                    // Process the socket
                    _ = ProcessSocket(socket);
                }
            }
            catch { }

            ShutDown();
        }

        private async Task ProcessSocket(Socket socket)
        {
            var peer = new AsyncPeer(socket);
            _peers[peer.PeerId] = peer;

            try
            {
                await peer.Process().ConfigureAwait(false);
            }
            catch { }

            await RemovePeer(peer).ConfigureAwait(false);
        }

        public void ShutDown()
        {
            _alive = false;

            // If we never connect listener.Shutdown throws an error, so try separately
            try { _listener.Shutdown(SocketShutdown.Both); } catch { }
            try { _listener.Close(); } catch { }

            if (_peers.Count == 0)
                return;

            // Send Kill Signals to the Peer Sockets
            foreach (var kv in _peers)
            {
                kv.Value.ShutDown();
            }
        }

        public async Task RemovePeer(AsyncPeer peer, IMessage message = null)
        {
            if (_peers.TryRemove(peer.PeerId, out _))
            {
                var send = message != null ? message : AsyncTcp.HeaderMessage(AsyncTcp.ErrorType);
                await peer.Send(send).ConfigureAwait(false);
            }
        }
    }
}