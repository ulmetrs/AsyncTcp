using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Threading.Tasks;
using static AsyncTcp.Utils;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;
        private readonly int _recvBufferSize;
        private readonly int _keepAliveInterval;

        private string _hostName;
        private int _bindPort;

        public AsyncPeer _serverPeer;
        private bool _clientRunning = false;
        private Channel<Type>

        public AsyncClient(
            IAsyncHandler handler,
            int recvBufferSize = 1024,
            int keepAliveInterval = 10)
        {
            _handler = handler ?? throw new Exception("Handler cannot be null");
            _recvBufferSize = recvBufferSize;
            _keepAliveInterval = keepAliveInterval;
        }

        public void Send<T>(int type, T data)
        {
            _serverPeer.Send(type, data);
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
            var buffer = new byte[_recvBufferSize];
            var segment = new ArraySegment<byte>(buffer, 0, _recvBufferSize);
            // Use the TaskExtensions for await receive
            int bytesRead;
            try
            {
                while ((bytesRead = await _serverPeer.Socket.ReceiveAsync(segment, 0).ConfigureAwait(false)) > 0)
                {
                    // Process the bytes that we do have, could be an entire message, a partial message split because of tcp,
                    // or partial message split because of buffer size
                    await _serverPeer.ProcessBytes(buffer, bytesRead, _handler).ConfigureAwait(false);
                }
            }
            catch
            { }
            // We stopped receiving bytes, meaning we disconnected
            await ShutDown().ConfigureAwait(false);
            // Wait for keep alive to finish
            await Task.WhenAll(keepAlive).ConfigureAwait(false);
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

            socket.NoDelay = true;

            var peer = new AsyncPeer(socket);

            try
            { await _handler.PeerConnected(peer).ConfigureAwait(false); }
            catch (Exception e)
            { await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false); }

            return peer;
        }

        public void Stop()
        {
            _clientRunning = false;

            try
            {
                _serverPeer.Socket.Shutdown(SocketShutdown.Both);
                _serverPeer.Socket.Close();
            }
            catch { }
        }

        public async Task ShutDown()
        {
            if (_serverPeer != null)
            {
                try
                {
                    _serverPeer.Socket.Shutdown(SocketShutdown.Both);
                    _serverPeer.Socket.Close();
                }
                catch { }

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
                    await _serverPeer.SendKeepAliveAsync().ConfigureAwait(false);
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