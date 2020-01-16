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
            int keepAliveInterval = 10)
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
                await LogMessageAsync("Caught ConnectAsync Exception", true).ConfigureAwait(false);
                return;
            }

            _clientRunning = true;

            _serverPeer = new AsyncPeer(socket, _handler);

            var keepAlive = Task.Run(KeepAlive);

            await _serverPeer.Process().ConfigureAwait(false);

            ShutDown();

            await keepAlive.ConfigureAwait(false);
        }

        public void ShutDown()
        {
            _clientRunning = false;

            if (_serverPeer != null)
            {
                _serverPeer.ShutDown();

                _serverPeer = null;
            }
        }

        public Task Send(int type, object data = null)
        {
            return _serverPeer.Send(type, data);
        }

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;

            while (_clientRunning)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                
                if (count == _keepAliveInterval)
                {
                    count = 0;

                    await _serverPeer.Send(0).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }
            }
        }
    }
}