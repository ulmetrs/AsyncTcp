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

        private string _hostName;
        private int _bindPort;

        public AsyncPeer _serverPeer;
        private bool _clientRunning = false;

        public AsyncClient(
            IAsyncHandler handler,
            int keepAliveInterval = 10)
        {
            if (!AsyncTcp.Initialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _keepAliveInterval = keepAliveInterval;
        }

        public Task Send(int type, object data)
        {
            return _serverPeer.Send(type, data);
        }

        public async Task Start(string hostname, int bindPort = 9050, bool findDnsMatch = false)
        {
            _hostName = hostname;
            _bindPort = bindPort;

            _serverPeer = await CreateAndConnectServerPeerAsync(findDnsMatch).ConfigureAwait(false);

            // Set client running
            _clientRunning = true;
            // Start our keep-alive thread
            var keepAlive = Task.Run(KeepAlive);
            // Dedicated buffer for async reads
            await _serverPeer.Process().ConfigureAwait(false);
            // We stopped receiving bytes, meaning we disconnected
            await ShutDown().ConfigureAwait(false);
            // Wait for keep alive to finish
            await keepAlive.ConfigureAwait(false);
        }

        private async Task<(Socket, IPEndPoint)> BuildSocketAndEndpoint(bool findDnsMatch)
        {
            Socket socket = null;
            IPEndPoint remoteEndpoint = null;

            if (findDnsMatch)
            {
                var ipHostInfo = Dns.GetHostEntry(_hostName);

                foreach (var address in ipHostInfo.AddressList)
                {
                    // Break on first IPv4 address.
                    // InterNetworkV6 for IPv6
                    if (address.ToString() == _hostName)
                    {
                        remoteEndpoint = new IPEndPoint(address, _bindPort);
                        socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        break;
                    }
                }
            }

            // Default Scenario:
            // If Socket is still not defined, acquire first IPAddreses resolved by DNS and attempt connection
            // that way.
            if (socket == null)
            {
                var ipAddress = Dns.GetHostEntry(_hostName).AddressList[0];

                await LogMessageAsync(string.Format(HostnameMessage, _hostName, ipAddress, _bindPort)).ConfigureAwait(false);

                remoteEndpoint = new IPEndPoint(ipAddress, _bindPort);
                socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            }

            return (socket, remoteEndpoint);
        }

        private async Task<AsyncPeer> CreateAndConnectServerPeerAsync(bool findDnsMatch)
        {
            (Socket socket, IPEndPoint remoteEndpoint) = await BuildSocketAndEndpoint(findDnsMatch).ConfigureAwait(false);

            await socket.ConnectAsync(remoteEndpoint).ConfigureAwait(false);

            var peer = new AsyncPeer(socket, _handler);

            try
            { await _handler.PeerConnected(peer).ConfigureAwait(false); }
            catch (Exception e)
            { await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false); }

            return peer;
        }

        public async Task ShutDown()
        {
            _clientRunning = false;
            if (_serverPeer != null)
            {
                _serverPeer.ShutDown();

                try
                { await _handler.PeerDisconnected(_serverPeer).ConfigureAwait(false); }
                catch (Exception e)
                { await LogErrorAsync(e, PeerRemovedErrorMessage).ConfigureAwait(false); }

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