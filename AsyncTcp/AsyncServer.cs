using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.IO;

namespace AsyncTcp
{
    public class AsyncServer
    {
        // Handler
        private AsyncHandler _handler;
        // List of Peers
        public List<AsyncPeer> _peers;
        // Lock for _peers
        private object _peerLock;
        // Server thread
        private Thread _server;
        // Server kill bool
        private bool _stopServer;
        // Server port
        private int _port;
        // Thread signal
        private ManualResetEvent _allDone;
        // Keep Alive Thread
        private Thread _keepAlive;
        // Keep Alive Time
        private int _keepAliveTime;

        // TODO implement keep alive message handling from the client
        // TODO implement a polling thread that regularly cleans up half-open sockets (I think this should work)

        public AsyncServer(AsyncHandler handler, int keepAliveTimeMs) {
            _handler = handler;
            _peers = new List<AsyncPeer>();
            _peerLock = new object();
            _server = new Thread(() => Server());
            _stopServer = false;
            _allDone = new ManualResetEvent(false);
            _keepAlive = new Thread(() => KeepAlive());
            _keepAliveTime = keepAliveTimeMs;
        }

        public void Start(int port)
        {
            if (IsRunning())
            {
                return;
            }

            _stopServer = false;
            _port = port;
            
            // Start our server thread
            _server.Start();
        }

        public void Stop()
        {
            _stopServer = true;
        }

        public bool IsRunning()
        {
            return _server.IsAlive || _keepAlive.IsAlive;
        }

        private void Server()
        {
            try
            {
                // Establish the local endpoint for the socket.  
                // The DNS name of the computer 
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _port);

                Console.WriteLine("hostname : " + Dns.GetHostName() + "   ip : " + ipAddress + "   port : " + _port);
 
                // Create a TCP/IP socket.  
                Socket listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                // Bind the socket to the local endpoint and listen for incoming connections.
                listener.Bind(localEndPoint);
                listener.Listen(100);

                // Start our keep-alive thread
                _keepAlive.Start();

                while (true)
                {
                    if (_stopServer)
                    {
                        return;
                    }
                    // Set the event to nonsignaled state.  
                    _allDone.Reset();

                    // Start an asynchronous socket to listen for connections.  
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);

                    // Wait until a connection is made before continuing.  TODO give a timeout so we can exit if server stop was requested
                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        private void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.  
            _allDone.Set();

            // Get the socket that handles the client request.  
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            handler.NoDelay = true;
            // TODO investigate other socket settings

            // Create the state object.  
            AsyncPeer peer = new AsyncPeer();
            peer.socket = handler;

            // Add to the list of peers
            lock (_peerLock)
            {
                _peers.Add(peer);
            }

            // Callback to AsyncHandler, should we do this on a new task?
            _handler.PeerConnected(peer);

            // Begin async receiving data
            handler.BeginReceive(peer.recvBuffer, 0, 1024, 0, new AsyncCallback(ReadCallback), peer);
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
                    Debug.WriteLine("EndSend Received 0 bytes, Removing Peer : " + peer.socket.LocalEndPoint);
                    RemovePeer(peer);
                }

                // Log non-keepalives (REMOVE)
                if (numBytes > 8)
                {
                    Console.WriteLine("Byte {0} to {1} out of {2} received", peer.stream.Position, peer.stream.Position + numBytes, peer.dataSize);
                }

                // Add the read bytes to the current stream
                peer.stream.Write(peer.recvBuffer, 0, numBytes);

                ParseRead(peer);
            }
            catch
            {
                Debug.WriteLine("EndReceive Error, Removing Peer : " + peer.socket.LocalEndPoint);
                RemovePeer(peer);
            }
        }

        private void ParseRead(AsyncPeer peer) {

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
                if (peer.dataSize > 0)
                {
                    // Store our write position
                    long pos = peer.stream.Position;
                    // Seek to the beginning of our data (byte 8)
                    peer.stream.Seek(8, SeekOrigin.Begin);
                    // Create a data-sized array for our callback
                    byte[] data = new byte[peer.dataSize];
                    // Read up to our data boundary
                    peer.stream.Read(data, 0, peer.dataSize);
                    // TODO should we handle in a new task? do we need to?
                    _handler.DataReceived(peer, peer.dataType, peer.dataSize, data);
                }

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

        public void Send(AsyncPeer peer, int dataType, int dataSize, byte[] data)
        {
            // Spin until we can safely send data (We have a polling mechanism sending keepalive messages)
            SpinWait.SpinUntil(() => peer.sendIndex == -1);
            // Set our send index
            peer.sendIndex = 0;
            // Set our state buffer
            peer.sendBuffer = new byte[dataSize + 8];
            using (MemoryStream stream = new MemoryStream(peer.sendBuffer))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(dataType);
                    writer.Write(dataSize);
                    // We have no data in keep alive packets
                    if (data != null)
                    {
                        writer.Write(data, 0, dataSize);
                    }    
                }
            }
            // Begin sending the data to the remote device.  
            peer.socket.BeginSend(peer.sendBuffer, 0, peer.sendBuffer.Length, 0, new AsyncCallback(SendCallback), peer);
        }

        private void SendCallback(IAsyncResult ar)
        {
            // Retrieve the peer state object
            AsyncPeer peer = (AsyncPeer)ar.AsyncState;

            try
            {
                // Complete sending the data to the remote device.  
                int numBytes = peer.socket.EndSend(ar);

                // Log actual messages (REMOVE)
                if (peer.sendBuffer.Length > 8)
                {
                    Console.WriteLine("Byte {0} to {1} out of {2} sent", peer.sendIndex, peer.sendIndex + numBytes, peer.sendBuffer.Length);
                }
                
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
            catch
            {
                Debug.WriteLine("EndSend Error, Removing Peer : " + peer.socket.LocalEndPoint);
                RemovePeer(peer);
            }
        }

        private void RemovePeer(AsyncPeer peer)
        {
            // Callback to AsyncHandler, should we do this on a new task?
            // If so make sure to not access socket reference
            _handler.PeerDisconnected(peer);
            // Close the socket on our end
            peer.socket.Shutdown(SocketShutdown.Both);
            peer.socket.Close();
            // Remove our peer
            lock (_peerLock)
            {
                _peers.Remove(peer);
            }
        }

        private void KeepAlive()
        {
            // Garbage collector should be deleting these copies, perhaps should be more efficient way?
            List<AsyncPeer> peers;
            while (true)
            {
                if (_stopServer)
                {
                    return;
                }
                // Keep Alive timer
                Thread.Sleep(_keepAliveTime);
                // Make a shallow copy of our peers list
                lock (_peers)
                {
                    peers = new List<AsyncPeer>(_peers);
                }
                // Iterate over our copy and send keep-alive messages
                foreach (AsyncPeer peer in peers)
                {
                    Send(peer, 0, 0, null);
                }
            }
        }
    }
}