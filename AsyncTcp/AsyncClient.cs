using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;
        private readonly int _keepAliveInterval;

        private Socket _socket;
        private AsyncPeer _peer;
        private bool _alive;

        public AsyncClient(
            IAsyncHandler handler,
            int keepAliveInterval = AsyncTcp.KeepAliveInterval)
        {
            if (!AsyncTcp.IsInitialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _keepAliveInterval = keepAliveInterval;
        }

        public async Task Start(IPAddress address, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start client while alive");

            _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.NoDelay = true;
            await _socket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);

            _peer = new AsyncPeer(_socket, _handler);

            _alive = true;

            var keepAlive = Task.Run(KeepAlive);

            try
            {
                await _peer.Process().ConfigureAwait(false);
            }
            catch
            {
                ShutDown();
                await keepAlive.ConfigureAwait(false);
                throw;
            }

            ShutDown();
            await keepAlive.ConfigureAwait(false);
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

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;

            while (_alive)
            {
                await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);
                
                if (count == _keepAliveInterval)
                {
                    count = 0;

                    await _peer.Send(AsyncTcp.KeepAliveType).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }
            }
        }
    }
}