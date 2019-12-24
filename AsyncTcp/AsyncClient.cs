using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient : AsyncBase
    {
        private int _recvBufferSize;
        private int _keepAliveTimeMs;

        private string _hostName;
        private int _bindPort;
        private Task _keepAlive;

        public AsyncPeer _server;
        private bool _clientRunning = false;

        public AsyncClient(
            AsyncHandler handler,
            int recvBufferSize = 1024,
            int keepAliveTimeMs = 5000) {

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _recvBufferSize = recvBufferSize;
            _keepAliveTimeMs = keepAliveTimeMs;
        }

        public async Task Start(string hostname, int bindPort = 9050)
        {
            _hostName = hostname;
            _bindPort = bindPort;
            // Connect to a remote device.
            IPHostEntry ipHostInfo = Dns.GetHostEntry(_hostName);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint remoteEndpoint = new IPEndPoint(ipAddress, _bindPort);
            Console.WriteLine("hostname : " + _hostName + "   ip : " + ipAddress + "   port : " + _bindPort);
            // Create a TCP/IP socket.  
            Socket socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // Connect
            await socket.ConnectAsync(remoteEndpoint);
            // Disable Nagles
            socket.NoDelay = true;
            // Create the peer
            var peer = new AsyncPeer(socket, _recvBufferSize);
            // Set server variable
            _server = peer;
            // Handle Peer Connected
            try
            {
                await _handler.PeerConnected(peer); // Test configure await here
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
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
                while (_clientRunning && (bytesRead = await socket.ReceiveAsync(segment, 0)) > 0)
                {
                    // Write our buffer bytes to the peer's message stream
                    peer.stream.Write(buffer, 0, bytesRead);
                    // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                    await ParseReceive(peer);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Receive Error: " + e.ToString());
            }
            // We stopped receiving bytes, meaning we disconnected
            await Disconnect();
            // Wait for keep alive to finish
            Task.WaitAll(_keepAlive);
        }

        public async Task Disconnect()
        {
            if (_server != null)
            {
                try
                {
                    _server.socket.Shutdown(SocketShutdown.Both);
                    _server.socket.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                try
                {
                    await _handler.PeerDisconnected(_server);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                _server = null;
            }
            _clientRunning = false;
        }

        private async Task KeepAlive()
        {
            while (_clientRunning)
            {
                Thread.Sleep(_keepAliveTimeMs);
                await Send(_server, 0, 0, null);
            }
        }
    }
}