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

            Console.WriteLine("hostname : " + _hostName + "   ip : " + ipAddress + "   port : " + _bindPort);
            // Establish the remote endpoint for the socket.
            IPEndPoint remoteEndpoint = new IPEndPoint(ipAddress, _bindPort);
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
                    peer.Stream.Write(buffer, 0, bytesRead);
                    // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                    await ParseReceive(peer);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Receive Error: " + e.ToString());
            }
            // We stopped receiving bytes, meaning we disconnected
            await RemovePeer();
            // Wait for keep alive to finish
            Task.WaitAll(_keepAlive);
        }

        public void Stop()
        {
            // FIXME Im not sure the peer loop will exit properly, check this
            _clientRunning = false;
        }

        private async Task RemovePeer()
        {
            if (_server != null)
            {
                // Close the socket on our end
                try
                {
                    _server.Socket.Shutdown(SocketShutdown.Both);
                    _server.Socket.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                // Handler Callback for peer disconnected
                try
                {
                    await _handler.PeerDisconnected(_server);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
            _server = null;
        }

        public Task Send(int dataType, int dataSize, byte[] data)
        {
            return Send(_server, dataType, dataSize, data);
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