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
        private AsyncPeer _serverPeer;

        private bool _clientRunning;

        public string HostName { get; private set; }

        public AsyncClient(
            IAsyncHandler handler,
            int keepAliveInterval = 10)
        {
            if (!AsyncTcp.IsInitialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _keepAliveInterval = keepAliveInterval;
        }

        public Task Send(int type, object data)
        {
            return _serverPeer.Send(type, data);
        }

        public async Task Start(string hostName, int bindPort = 9050, bool findDnsMatch = false)
        {
            var address = await Utils.GetIPAddress(hostName, findDnsMatch).ConfigureAwait(false);

            var remoteEndpoint = new IPEndPoint(address, bindPort);
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            await socket.ConnectAsync(remoteEndpoint).ConfigureAwait(false);

            HostName = address.ToString();

            _clientRunning = true;

            _serverPeer = new AsyncPeer(socket, _handler);

            try
            {
                await _handler.PeerConnected(_serverPeer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false);
            }

            var keepAlive = Task.Run(KeepAlive);

            await _serverPeer.Process().ConfigureAwait(false);

            await ShutDown().ConfigureAwait(false);

            await keepAlive.ConfigureAwait(false);
        }

        

        public async Task ShutDown()
        {
            _clientRunning = false;

            if (_serverPeer != null)
            {
                _serverPeer.ShutDown();

                try
                {
                    await _handler.PeerDisconnected(_serverPeer).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    await LogErrorAsync(e, PeerRemovedErrorMessage).ConfigureAwait(false);
                }
                
                _serverPeer = null;
            }
        }

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;
            while (_clientRunning)
            {
                // Send Keep Alives every interval
                if (count == _keepAliveInterval)
                {
                    await _serverPeer.Send(0).ConfigureAwait(false);
                    count = 0;
                }
                else
                {
                    count++;
                }
                // Check every second for exit
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}