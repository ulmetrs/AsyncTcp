using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;

        private AsyncPeer _peer;
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

            _peer = new AsyncPeer(socket, _handler);

            try
            {
                await _peer.Process().ConfigureAwait(false);
            }
            catch { }

            ShutDown();
        }

        public Task Send(int type, object data = null)
        {
            // Since we cannot guarantee that queued sends will even be sent out after they are queued (socket error/disconnect),
            // it doesn't make sense to throw here indicating failed queued messages after shutdown, since its a half-way solution to that problem
            if (!_alive)
                return Task.CompletedTask;

            return _peer.Send(type, data);
        }

        public void ShutDown()
        {
            _alive = false;

            _peer.ShutDown();
        }
    }
}