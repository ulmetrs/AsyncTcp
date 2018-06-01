using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncClient
    {
        // Handler
        protected AsyncHandler _handler;
        // Keep Alive Time
        protected int _keepAliveTime;
        // Keep Alive Task
        protected Task _keepAlive;
        
        // Server Peer
        public AsyncPeer _server;
        // Server host
        private string _hostName;
        // Server port
        private int _port;
        // Client kill bool
        private bool _stopClient;
        // Thread signal
        private ManualResetEvent _allDone;

        public AsyncClient(AsyncHandler handler, int keepAliveTimeMs)
        {
            _handler = handler;
            _keepAliveTime = keepAliveTimeMs;
            _allDone = new ManualResetEvent(false);
        }

        public Task Start(string hostname, int port)
        {
            _stopClient = false;
            _hostName = hostname;
            _port = port;

            // Start our client thread
            return Task.Run(() => Connect());
        }

        private void Connect()
        {
            try
            {
                // Connect to a remote device.
                IPHostEntry ipHostInfo = Dns.GetHostEntry(_hostName);
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, _port);

                Console.WriteLine("hostname : " + _hostName + "   ip : " + ipAddress + "   port : " + _port);

                // Create a TCP/IP socket.  
                Socket socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                // Set the event to nonsignaled state.  
                _allDone.Reset();

                // Connect to the remote endpoint.  
                socket.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), socket);
                _allDone.WaitOne(5000);

                if (_server == null)
                {
                    return;
                }

                /// Start our keep-alive thread
                _keepAlive = Task.Run(() => KeepAlive());

                while (true)
                {
                    if (_stopClient)
                    {
                        Task.WaitAll(_keepAlive);
                        return;
                    }
                    Thread.Sleep(3000);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("CONNECT ERROR " + e.ToString());
            }
        }

        public void Disconnect()
        {
            try
            {
                _handler.PeerDisconnected(_server);

                _server.socket.Shutdown(SocketShutdown.Both);
                _server.socket.Close();
                _server = null;
            }
            catch (Exception e)
            {
                Console.WriteLine("DISCONNECT ERROR " + e.ToString());
            }

            _stopClient = true;
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.  
                Socket socket = (Socket)ar.AsyncState;

                // Complete the connection.  
                socket.EndConnect(ar);
                socket.NoDelay = true;

                // Create the state object.  
                _server = new AsyncPeer();
                _server.socket = socket;

                // Callback to AsyncHandler, should we do this on a new task?
                _handler.PeerConnected(_server);

                // Begin async receiving data
                socket.BeginReceive(_server.recvBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), _server);
            }
            catch (Exception e)
            {
                Console.WriteLine("CONNECT CALLBACK ERROR " + e.ToString());
            }

            // Signal callback has finished 
            _allDone.Set();
        }

        private void ReadCallback(IAsyncResult ar)
        {
            // Retrieve the state object
            AsyncPeer peer = (AsyncPeer)ar.AsyncState;

            try
            {
                // Read data from the client socket.   
                int numBytes = peer.socket.EndReceive(ar);

                // I believe zero reads indicate the client has disconnected gracefully
                if (numBytes <= 0)
                {
                    //Console.WriteLine("EndReceived Received 0 bytes, Disconnecting : " + peer);
                    Disconnect();
                }
                
                // Add the read bytes to the current stream
                peer.stream.Write(peer.recvBuffer, 0, numBytes);

                ParseRead(peer);
            }
            catch (Exception e)
            {
                //Console.WriteLine("EndReceive Error : " + e.ToString() + "\nDisconnecting Peer: " + peer);
                Disconnect();
            }
        }

        private void ParseRead(AsyncPeer peer)
        {

            // We have not yet read our message header (data type and size) but have enough bytes to
            if (peer.dataSize < 0 && peer.stream.Position >= 8)
            {
                // Store our write position
                long writePos = peer.stream.Position;
                // Seek to the beginning of our data type
                peer.stream.Seek(0, SeekOrigin.Begin);
                // Read the data type and size ints
                BinaryReader reader = new BinaryReader(peer.stream); // We don't want to close the stream, so no 'using' statement
                peer.dataType = reader.ReadInt32();
                peer.dataSize = reader.ReadInt32();
                // Seek back to our current write position
                peer.stream.Seek(writePos, SeekOrigin.Begin);
            }

            // We have more data to read
            if (peer.dataSize < 0 || (peer.stream.Position < (peer.dataSize + 8)))
            {
                peer.socket.BeginReceive(peer.recvBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), peer);
            }
            // We have read enough data to complete a message
            else
            {
                byte[] data = null;
                // If we actually have a payload
                if (peer.dataSize > 0)
                {
                    // Store our write position
                    long pos = peer.stream.Position;
                    // Seek to the beginning of our data (byte 8)
                    peer.stream.Seek(8, SeekOrigin.Begin);
                    // Create a data-sized array for our callback
                    data = new byte[peer.dataSize];
                    // Read up to our data boundary
                    peer.stream.Read(data, 0, peer.dataSize);
                }
                // TODO should we handle in a new task? do we need to?
                _handler.DataReceived(peer, new DataPacket() { dataType = peer.dataType, dataSize = peer.dataSize, data = data });
                // Reset our state variables
                peer.dataType = -1;
                peer.dataSize = -1;
                // Create a new stream
                MemoryStream newStream = new MemoryStream();
                // Copy all remaining data to the new stream
                peer.stream.CopyTo(newStream);
                // Dispose our old stream
                peer.stream.Dispose();
                // Set the peer's stream to the new stream
                peer.stream = newStream;
                // Parse the new stream, our stream may have contained multiple messages
                ParseRead(peer);
            }
        }

        public void Send(DataPacket packet)
        {
            // Sanity check, client should know not to send messages to disconnected server
            if (_server == null || _server.socket == null)
            {
                return;
            }
            // Spin until we can safely send data (We have a polling mechanism sending keepalive messages)
            SpinWait.SpinUntil(() => _server.sendIndex == -1);
            // Set our send index
            _server.sendIndex = 0;
            // Set our state buffer
            _server.sendBuffer = new byte[packet.dataSize + 8];
            using (MemoryStream stream = new MemoryStream(_server.sendBuffer))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(packet.dataType);
                    writer.Write(packet.dataSize);
                    // We have no data in keep alive packets
                    if (packet.data != null)
                    {
                        writer.Write(packet.data, 0, packet.dataSize);
                    }
                }
            }
            // Begin sending the data to the remote device.  
            _server.socket.BeginSend(_server.sendBuffer, 0, _server.sendBuffer.Length, 0, new AsyncCallback(SendCallback), _server);
        }

        private void SendCallback(IAsyncResult ar)
        {
            // Retrieve the peer state object
            AsyncPeer peer = (AsyncPeer)ar.AsyncState;

            try
            {
                // Complete sending the data to the remote device.  
                int numBytes = peer.socket.EndSend(ar);

                // Increment our send index
                peer.sendIndex += numBytes;
                // We are not done sending message
                if (peer.sendIndex < peer.sendBuffer.Length)
                {
                    // Begin sending the data to the remote device.  
                    peer.socket.BeginSend(peer.sendBuffer, peer.sendIndex, (peer.sendBuffer.Length - peer.sendIndex), 0, new AsyncCallback(SendCallback), peer);
                }
                else
                {
                    // Remove our send buffer
                    peer.sendBuffer = null;
                    // Reset our send index
                    peer.sendIndex = -1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("SEND CALLBACK ERROR " + e.ToString() + "\nDisconnecting Peer " + peer);
                Disconnect();
            }
        }

        private void KeepAlive()
        {
            while (true)
            {
                Thread.Sleep(_keepAliveTime);
                if (_server == null || _server.socket == null)
                {
                    return;
                }
                Send(new DataPacket());
            }
        }
    }
}