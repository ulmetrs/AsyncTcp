﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncServer : AsyncBase
    {
        private int _recvBufferSize;
        private int _keepAliveTimeMs;

        private IPAddress _ipAddress;
        private int _bindPort;
        private Task _keepAlive;

        private readonly List<AsyncPeer> _peers = new List<AsyncPeer>();
        private bool _serverRunning = false;

        public AsyncServer(
            AsyncHandler handler,
            int recvBufferSize = 1024,
            int keepAliveTimeMs = 5000) {

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _recvBufferSize = recvBufferSize;
            _keepAliveTimeMs = keepAliveTimeMs;
        }

        public async Task Start(IPAddress ipAddress = null, int bindPort = 9050)
        {
            _ipAddress = ipAddress;
            if (_ipAddress == null)
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                _ipAddress = ipHostInfo.AddressList[0];
            }
            _bindPort = bindPort;

            Console.WriteLine("Hostname : " + Dns.GetHostName() + "   ip : " + _ipAddress + "   port : " + _bindPort);
            // Establish the local endpoint for the socket.
            IPEndPoint localEndPoint = new IPEndPoint(_ipAddress, _bindPort);
            // Create a TCP/IP socket.  
            Socket listener = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // Bind the socket to the local endpoint and listen for incoming connections.
            listener.Bind(localEndPoint);
            listener.Listen(100);

            // Set server running
            _serverRunning = true;

            // Start our keep-alive thread
            _keepAlive = Task.Run(KeepAlive);

            // Accept all connections while server running
            while (_serverRunning)
            {
                // Handle each accepted socket
                using (Socket socket = await listener.AcceptAsync())
                {
                    // Disable Nagles
                    socket.NoDelay = true;
                    // Create the peer
                    var peer = new AsyncPeer(socket, _recvBufferSize);
                    // Add to the list of peers for keep-alive messaging
                    lock (_peers)
                    {
                        _peers.Add(peer);
                    }
                    Console.WriteLine("Added to peer list, New num peers : " + _peers.Count);
                    // Handler Callback for peer connected
                    try
                    {
                        await _handler.PeerConnected(peer);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                    // Dedicated buffer for async reads
                    var buffer = new byte[_recvBufferSize];
                    var segment = new ArraySegment<byte>(buffer, 0, _recvBufferSize);
                    // Use the TaskExtensions for await receive
                    int bytesRead;
                    try
                    {
                        // Keep Receving Bytes, Stop if server or stopped or peer inactive (Check if this gracefully closes!)
                        while (_serverRunning && peer.Active && (bytesRead = await peer.Socket.ReceiveAsync(segment, 0)) > 0) 
                        {
                            // Write our buffer bytes to the peer's message stream
                            peer.Stream.Write(buffer, 0, bytesRead);
                            // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                            await ParseReceive(peer);
                        }
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine("Receive Error: " + e.ToString());
                    }
                    // We stopped receiving bytes, meaning we disconnected.  Remove the Peer.
                    await RemovePeer(peer);
                }
            }
            // Wait for keep alive to finish
            Task.WaitAll(_keepAlive);
        }

        public void Stop()
        {
            // FIXME Im not sure the main loop will exit properly, check this
            _serverRunning = false;
        }

        public void Shutdown(AsyncPeer peer)
        {
            // FIXME Im not sure the peer loop will exit properly, check this
            peer.Active = false;
        }

        private async Task RemovePeer(AsyncPeer peer)
        {
            bool removed = false;
            lock (_peers)
            {
                removed = _peers.Remove(peer);
            }
            if (removed)
            {
                // Close the socket on our end
                try
                {
                    peer.Socket.Shutdown(SocketShutdown.Both);
                    peer.Socket.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                // Handler Callback for peer disconnected
                try
                {
                    await _handler.PeerDisconnected(peer);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }

        private async Task KeepAlive()
        {
            while (_serverRunning)
            {
                Thread.Sleep(_keepAliveTimeMs);
                // Lock and duplicate our peers list
                List<AsyncPeer> copy;
                lock (_peers)
                {
                    copy = new List<AsyncPeer>(_peers);
                }
                // Iterate over our copy and send keep-alive messages
                foreach (AsyncPeer peer in copy)
                {
                    await Send(peer, 0, 0, null);
                }
            }
        }
    }
}