using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Logging;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;
        private readonly int _keepAliveInterval;
        private AsyncPeer _peer;

        private Socket _socket;
        private bool _clientRunning;

        public string HostName { get; private set; }

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
            if (_clientRunning)
                throw new Exception("Cannot Start, Client is running");

            try
            {
                _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _socket.NoDelay = true;
                await _socket.ConnectAsync(new IPEndPoint(address, bindPort)).ConfigureAwait(false);

                HostName = address.ToString();
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, "Connect Exception", false).ConfigureAwait(false);
                return;
            }

            await LogMessageAsync(string.Format(HostnameMessage, HostName, address, bindPort), false).ConfigureAwait(false);

            _clientRunning = true;

            _peer = new AsyncPeer(_socket, _handler);

            var keepAlive = Task.Run(KeepAlive);

            await _peer.Process().ConfigureAwait(false);

            ShutDown();

            await keepAlive.ConfigureAwait(false);

            await LogMessageAsync(string.Format("Finished AsyncClient Task"), false).ConfigureAwait(false);
        }

        public Task Send(int type, object data = null)
        {
            // Since we cannot guarantee that queued sends will even be sent out after they are queued (socket error/disconnect),
            // it doesn't make sense to throw here indicating failed queued messages after shutdown, since its a half-way solution to that problem
            if (!_clientRunning)
                return Task.CompletedTask;

            return _peer.Send(type, data);
        }

        public void ShutDown()
        {
            _clientRunning = false;

            _peer.ShutDown();
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