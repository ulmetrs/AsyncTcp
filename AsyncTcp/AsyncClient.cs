using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;

        public AsyncPeer Peer { get; private set; }

        private bool _alive;

        public AsyncClient(IAsyncHandler handler)
        {
            if (!AsyncTcp.IsInitialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
        }

        public async Task Start(IPAddress address, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start client while alive");

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            await socket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);

            _alive = true;

            Peer = new AsyncPeer(socket, _handler);

            try
            {
                await Peer.Process().ConfigureAwait(false);
            }
            catch { }

            ShutDown();
        }

        public void ShutDown()
        {
            _alive = false;

            Peer.ShutDown();
        }
    }
}