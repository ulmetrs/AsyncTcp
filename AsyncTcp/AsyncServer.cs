using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncServer
    {
        // Handler
        private AsyncHandler _handler;
        // Keep Alive Time
        private int _keepAliveTime;
        // Keep Alive Task
        private Task _keepAlive;

        // List of Peers
        public List<AsyncPeer> _peers;
        // Lock for _peers
        private object _peerLock;
        // Listening port
        private int _port;
        // Server kill bool
        private bool _stopServer;
        // Thread signal
        private ManualResetEvent _allDone;

        public AsyncServer(AsyncHandler handler, int keepAliveTimeMs) {
            _handler = handler;
            _keepAliveTime = keepAliveTimeMs;

            _peers = new List<AsyncPeer>();
            _peerLock = new object();
            _stopServer = false;
            _allDone = new ManualResetEvent(false);
        }

        public Task Start(int port)
        {
            _stopServer = false;
            _port = port;

            // Start our server thread
            return Task.Run(() => Accept());
        }

        public void Stop()
        {
            _stopServer = true;
        }

        private void Accept()
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
                _keepAlive = Task.Run(() => KeepAlive());

                while (true)
                {
                    if (_stopServer)
                    {
                        Task.WaitAll(_keepAlive);
                        return;
                    }

                    //Console.WriteLine("Begin Accepting New Socket, Open Handles : " + Process.GetCurrentProcess().HandleCount);

                    // Set the event to nonsignaled state.  
                    _allDone.Reset();

                    // Start an asynchronous socket to listen for connections.  
                    //Console.WriteLine("Waiting for a connection...");
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
            handler.BeginReceive(peer.recvBuffer, 0, 1024, 0, new AsyncCallback(ReceiveCallback), peer);
        }

        private void ReceiveCallback(IAsyncResult ar)
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
                    //Console.WriteLine("EndReceived Received 0 bytes, Removing Peer : " + peer);
                    RemovePeer(peer);
                }

                // Add the read bytes to the current stream
                peer.stream.Write(peer.recvBuffer, 0, numBytes);

                ParseReceive(peer);
            }
            catch (Exception)
            {
                //Console.WriteLine("EndReceive Error : " + e.ToString() + "\nRemoving Peer: " + peer);
                RemovePeer(peer);
            }
        }

        private void ParseReceive(AsyncPeer peer)
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
                peer.socket.BeginReceive(peer.recvBuffer, 0, 1024, 0, new AsyncCallback(ReceiveCallback), peer);
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
                // TODO should we handle in a new task?
                _handler.DataReceived(peer, peer.dataType, peer.dataSize, data);
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
                ParseReceive(peer);
            }
        }

        // Send a message to the remote peer
        public void Send(AsyncPeer peer, int dataType, int dataSize, byte[] data)
        {
            // Sanity check, server should know not to send messages to disconnected clients
            if (peer.socket == null)
            {
                return;
            }
            // Spin until we can safely send data, our keepalives and rapid sends need not conflict
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
                // TODO maybe we can wait for receive to remove peer
                Console.WriteLine("EndSend Error " + e.ToString() + "\nRemoving Peer : " + peer);
                RemovePeer(peer);
            }
        }

        private void RemovePeer(AsyncPeer peer)
        {
            if (peer.socket == null)
            {
                return;
            }
            try
            {
                // Callback to AsyncHandler, should we do this on a new task?
                // If so make sure to not access socket reference
                _handler.PeerDisconnected(peer);
                // Close the socket on our end
                peer.socket.Shutdown(SocketShutdown.Both);
                peer.socket.Close();
                peer.socket = null;
                // Remove our peer
                lock (_peerLock)
                {
                    _peers.Remove(peer);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
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
                // Keep Alive timer TODO allocate for actual time (it takes time to iterate and send messages)
                Thread.Sleep(_keepAliveTime);
                // Make a shallow copy of our peers list
                // TODO is it necessary that we lock this?
                lock (_peers)
                {
                    peers = new List<AsyncPeer>(_peers);
                }
                // Iterate over our copy and send keep-alive messages
                foreach (AsyncPeer peer in peers)
                {
                    // Don't send if our socket has been shut down, remember we iterated a shallow copy of our peers list
                    if (peer.socket != null)
                    {
                        Send(peer, 0, 0, null);
                    }
                }
            }
        }
    }
}