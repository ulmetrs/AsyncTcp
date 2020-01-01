using System;
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
        private int _keepAliveInterval;
        private int _taskCleanupInterval = 100;

        private IPAddress _ipAddress;
        private int _bindPort;

        private readonly List<AsyncPeer> _peers = new List<AsyncPeer>();
        private bool _serverRunning = false;

        public AsyncServer(
            AsyncHandler handler,
            int recvBufferSize = 1024,
            int keepAliveInterval = 10)
        {

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _recvBufferSize = recvBufferSize;
            _keepAliveInterval = keepAliveInterval;
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
            // Create a list of tasks
            var tasks = new List<Task>();
            // HUGE NOTE/DISCLAIMER: All Async Methods are run Synchronously until the first await,
            //  meaning we cant just do this first: tasks.Add(KeepAlive());
            // Add our Keep Alive Task
            tasks.Add(Task.Run(KeepAlive));
            // Count our tasks added
            var taskCount = 0;
            Socket socket;
            // Accept all connections while server running
            while (_serverRunning)
            {
                // Cleanup our tasks list, server can be long running so we don't wan't to append tasks forever
                if (taskCount >= _taskCleanupInterval)
                {
                    taskCount = 0;
                    var newTasks = new List<Task>();
                    for (int i = 0; i < tasks.Count; i++)
                    {
                        if (!tasks[i].IsCompleted)
                        {
                            newTasks.Add(tasks[i]);
                        }
                    }
                    tasks = newTasks;
                }
                // Synchronously await Accepts
                socket = await listener.AcceptAsync().ConfigureAwait(false);
                // Add Async Task Process Socket, this task will handle the new connection until it closes
                tasks.Add(ProcessSocket(socket));
                // Increment task count so we know when to run our task cleanup again
                taskCount++;
            }
            // Wait for all remaining tasks to finish
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ProcessSocket(Socket socket)
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
            // Handler Callback for peer connected
            try
            {
                await _handler.PeerConnected(peer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine("Peer Connected Error: " + e.ToString());
            }
            // Dedicated buffer for async reads
            var buffer = new byte[_recvBufferSize];
            var segment = new ArraySegment<byte>(buffer, 0, _recvBufferSize);
            // Use the TaskExtensions for await receive
            int bytesRead;
            try
            {
                // Loop Receive, Stopping when Server Stopped, Peer Shutdown, bytesRead = 0 (Client Graceful Shutdown), or Exception (Client Ungraceful Disconnect)
                while (_serverRunning && (bytesRead = await peer.Socket.ReceiveAsync(segment, 0).ConfigureAwait(false)) > 0)
                {
                    // Write our buffer bytes to the peer's message stream
                    peer.Stream.Write(buffer, 0, bytesRead);
                    // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                    await ParseReceive(peer).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Receive Error: " + e.ToString());
            }
            // Clean up the Peers socket and remove from list of peers
            await RemovePeer(peer).ConfigureAwait(false);
        }

        public async Task Stop()
        {
            // Stop the Accept Loop
            _serverRunning = false;

            if (_peers.Count == 0)
                return;

            List<AsyncPeer> copy;
            List<Task> tasks;
            // Lock and duplicate our peers list, that way we can process outside of lock
            lock (_peers)
            {
                copy = new List<AsyncPeer>(_peers);
            }
            // Distribute our RemovePeer tasks and wait until they are done
            tasks = new List<Task>();
            for (int i = 0; i < copy.Count; i++)
            {
                tasks.Add(RemovePeer(copy[i]));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task RemovePeer(AsyncPeer peer)
        {
            var removed = false;
            lock (_peers)
            {
                removed = _peers.Remove(peer);
            }
            // RemovePeer can be called from the server directly, upon closing the socket, ReceiveAsync will exit appropriately and call RemovePeer
            // again. With this check we only only call the handler methods once.
            if (removed)
            {
                // Close the socket on our end
                try
                {
                    // Let's wait until sends are complete before we shut down
                    await peer.SendLock.WaitAsync().ConfigureAwait(false);
                    peer.Socket.Shutdown(SocketShutdown.Both);
                    peer.Socket.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                finally
                {
                    peer.SendLock.Release();
                }
                // Handler Callback for peer disconnected
                try
                {
                    await _handler.PeerDisconnected(peer).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
            }
        }

        private async Task KeepAlive()
        {
            List<AsyncPeer> copy;
            List<Task> tasks;
            var count = _keepAliveInterval;
            while (_serverRunning)
            {
                // Send Keep Alives every interval
                if (count == _keepAliveInterval)
                {
                    count = 0;
                    if (_peers.Count == 0)
                        continue;

                    // Lock and duplicate our peers list, that way we can process outside of lock
                    lock (_peers)
                    {
                        copy = new List<AsyncPeer>(_peers);
                    }
                    // Distribute our SendKeepAlive tasks and wait until they are done
                    tasks = new List<Task>();
                    for (int i = 0; i < copy.Count; i++)
                    {
                        tasks.Add(SendKeepAlive(copy[i]));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }
                // Check every second for exit
                Thread.Sleep(1000);
            }
        }
    }
}