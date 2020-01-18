using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Logging;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;
        private readonly int _keepAliveInterval;

        private AsyncPeer _serverPeer;
        private bool _clientRunning;

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
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);
            }
            catch
            {
                await LogMessageAsync("Caught ConnectAsync Exception", false).ConfigureAwait(false);
                return;
            }

            _clientRunning = true;

            _serverPeer = new AsyncPeer(socket, _handler);

            var keepAlive = Task.Run(KeepAlive);

            await _serverPeer.Process().ConfigureAwait(false);

            await ShutDown().ConfigureAwait(false);

            await keepAlive.ConfigureAwait(false);
        }

        public Task Send(int type, object data = null)
        {
            return _serverPeer.Send(type, data);
        }

        public Task ShutDown(object data = null)
        {
            _clientRunning = false;

            return _serverPeer.Send(AsyncTcp.ErrorType, data);
        }

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;

            while (_clientRunning)
            {
                await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);
                
                if (count == _keepAliveInterval)
                {
                    count = 0;

                    await _serverPeer.Send(AsyncTcp.KeepAliveType).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }
            }
        }
    }
}