using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Utils;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncClient
    {
        private readonly IAsyncHandler _handler;
        private int _recvBufferSize;
        private int _keepAliveInterval;

        private string _hostName;
        private int _bindPort;
        private Task _keepAlive;

        public AsyncPeer _server;
        private bool _clientRunning = false;

        public AsyncClient(
            IAsyncHandler handler,
            int recvBufferSize = 1024,
            int keepAliveInterval = 10)
        {
            _handler = handler ?? throw new Exception("Handler cannot be null");
            _recvBufferSize = recvBufferSize;
            _keepAliveInterval = keepAliveInterval;
        }

        public Task Send(int dataType, int dataSize, byte[] data)
        {
            return _server.Send(dataType, dataSize, data);
        }

        public async Task Start(string hostname, int bindPort = 9050, bool findDnsMatch = true)
        {
            _hostName = hostname;
            _bindPort = bindPort;

            Socket socket = null;
            IPEndPoint remoteEndpoint = null;
            if (findDnsMatch)
            {
                var ipHostInfo = Dns.GetHostEntry(_hostName);

                foreach (var address in ipHostInfo.AddressList)
                {
                    //Break on first IPv4 address.
                    // InterNetworkV6 for IPv6
                    if (address.ToString() == _hostName)
                    {
                        remoteEndpoint = new IPEndPoint(address, _bindPort);
                        socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        break;
                    }
                }
            }

            if (socket == null)
            {
                // Connect to a remote device.
                IPHostEntry ipHostInfo = Dns.GetHostEntry(_hostName);
                IPAddress ipAddress = ipHostInfo.AddressList[0];

                await LogMessageAsync(string.Format(HostnameMessage, _hostName, ipAddress, _bindPort)).ConfigureAwait(false);

                //Trace.WriteLine("hostname : " + _hostName + "   ip : " + ipAddress + "   port : " + _bindPort);
                // Establish the remote endpoint for the socket.
                remoteEndpoint = new IPEndPoint(ipAddress, _bindPort);
                // Create a TCP/IP socket.  
                socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            }

            // Connect
            await socket.ConnectAsync(remoteEndpoint).ConfigureAwait(false);
            // Disable Nagles
            socket.NoDelay = true;
            // Create the peer
            _server = new AsyncPeer(socket, _recvBufferSize);
            // Handle Peer Connected
            try
            {
                await _handler.PeerConnected(_server).ConfigureAwait(false);
            }
            catch (Exception e)
            { await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false); }

            // Set client running
            _clientRunning = true;
            // Start our keep-alive thread
            _keepAlive = Task.Run(KeepAlive);
            // Dedicated buffer for async reads
            var buffer = new byte[_recvBufferSize];
            var segment = new ArraySegment<byte>(buffer, 0, _recvBufferSize);
            // Use the TaskExtensions for await receive
            int bytesRead;
            try
            {
                while ((bytesRead = await socket.ReceiveAsync(segment, 0).ConfigureAwait(false)) > 0)
                {
                    // Write our buffer bytes to the peer's message stream
                    _server.Stream.Write(buffer, 0, bytesRead);
                    // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                    await _server.ParseReceive(_handler).ConfigureAwait(false);
                }
            }
            catch
            {
                // Exception driven design I know, but need to work with what I got
            }
            // We stopped receiving bytes, meaning we disconnected
            await ShutDown().ConfigureAwait(false);
            // Wait for keep alive to finish
            Task.WaitAll(_keepAlive);
        }

        public void Stop()
        {
            // Turn off client running flag
            _clientRunning = false;

            // Send Kill Signal to the Server Socket
            try
            {
                _server.Socket.Shutdown(SocketShutdown.Both);
                _server.Socket.Close();
            }
            catch
            {
                // Do nothing
            }
        }

        public async Task ShutDown()
        {
            if (_server != null)
            {
                // Close the socket on our end
                try
                {
                    _server.Socket.Shutdown(SocketShutdown.Both);
                    _server.Socket.Close();
                }
                catch
                {
                    // Do nothing
                }
                // Handler Callback for peer disconnected
                try
                {
                    await _handler.PeerDisconnected(_server).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Peer Disconnected Error: " + e.ToString());
                }
                _server = null;
            }
        }

        private async Task KeepAlive()
        {
            int count = _keepAliveInterval;
            while (_clientRunning)
            {
                // Send Keep Alives every interval
                if (count == _keepAliveInterval)
                {
                    await _server.SendKeepAlive().ConfigureAwait(false);
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