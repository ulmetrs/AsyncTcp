using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Utils;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncServer
    {
        private readonly IAsyncHandler _handler;
        private readonly int _recvBufferSize;
        private readonly int _keepAliveInterval;
        private readonly int _taskCleanupInterval = 100;

        private IPAddress _ipAddress;
        private int _bindPort;
        private Socket _incomingConnectionListener;
        private bool _serverRunning;
        private readonly ConcurrentDictionary<long, AsyncPeer> _peers = new ConcurrentDictionary<long, AsyncPeer>();
        public string ServerHostName { get; private set; }

        public AsyncServer(
            IAsyncHandler handler,
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
            _bindPort = bindPort;
            if (_ipAddress == null)
            {
                var ipHostInfo = await Dns.GetHostEntryAsync(Dns.GetHostName()).ConfigureAwait(false);

                foreach (var address in ipHostInfo.AddressList)
                {
                    //Break on first IPv4 address.
                    // InterNetworkV6 for IPv6
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        _ipAddress = address;
                        ServerHostName = _ipAddress.ToString();
                        break;
                    }
                }
            }

            await LogMessageAsync(string.Format(HostnameMessage, ServerHostName, ipAddress, _bindPort)).ConfigureAwait(false);

            _incomingConnectionListener = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _incomingConnectionListener.Bind(new IPEndPoint(_ipAddress, _bindPort));
            _incomingConnectionListener.Listen(100);

            _serverRunning = true;
            // Create a list of tasks
            var tasks = new List<Task>
            {
                // HUGE NOTE/DISCLAIMER: All Async Methods are run Synchronously until the first await,
                //  meaning we cant just do this first: tasks.Add(KeepAlive());
                // Add our Keep Alive Task
                Task.Run(KeepAlive)
            };
            // Count our tasks added
            var taskCount = 0;
            Socket socket;
            try
            {
                // Accept all connections while server running
                while (_serverRunning && (socket = await _incomingConnectionListener.AcceptAsync().ConfigureAwait(false)) != null)
                {
                    // Add Async Task Process Socket, this task will handle the new connection until it closes
                    tasks.Add(ProcessSocket(socket));
                    // Increment task count so we know when to run our task cleanup again
                    taskCount++;
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
                }
            }
            catch
            {
                // Exception driven design I know, but need to work with what I got
            }
            // Wait for all remaining tasks to finish
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ProcessSocket(Socket socket)
        {
            socket.NoDelay = true;
            var peer = new AsyncPeer(socket);
            _peers[peer.PeerId] = peer;

            try
            {
                await _handler.PeerConnected(peer).ConfigureAwait(false);
            }
            catch (Exception e)
            { await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false); }

            // Dedicated buffer for async reads
            var buffer = new byte[_recvBufferSize];
            var segment = new ArraySegment<byte>(buffer, 0, _recvBufferSize);
            // Use the TaskExtensions for await receive
            int bytesRead;
            try
            {
                // Loop Receive, Stopping when Server Stopped, Peer Shutdown, bytesRead = 0 (Client Graceful Shutdown), or Exception (Server Force Remove/Shutdown)
                while (_serverRunning && (bytesRead = await peer.Socket.ReceiveAsync(segment, 0).ConfigureAwait(false)) > 0)
                {
                    // Write our buffer bytes to the peer's message stream
                    peer.Stream.Write(buffer, 0, bytesRead);
                    // Parse the bytes that we do have, could be an entire message, a partial message split because of tcp, or partial message split because of buffer size
                    await peer.ParseReceive(_handler).ConfigureAwait(false);
                }
            }
            catch
            {
                // Exception driven design I know, but need to work with what I got
            }
            // Clean up the Peers socket and remove from list of peers
            await RemovePeer(peer).ConfigureAwait(false);
        }

        public void Stop()
        {
            // Turn off server running flag
            _serverRunning = false;

            // Send Kill Signal to the Listener Socket
            try
            {
                _incomingConnectionListener.Shutdown(SocketShutdown.Both);
                _incomingConnectionListener.Close();
            }
            catch
            {
                // Do nothing
            }

            if (_peers.Count == 0)
                return;

            // Send Kill Signals to the Peer Sockets
            foreach (var kvp in _peers)
            {
                try
                {
                    kvp.Value.Socket.Shutdown(SocketShutdown.Both);
                    kvp.Value.Socket.Close();
                }
                catch { }
            }
        }

        public async Task RemovePeer(AsyncPeer peer)
        {
            // RemovePeer can be called from the server directly: upon closing the socket ReceiveAsync will exit appropriately and call RemovePeer
            // again. With this check we only only call the handler methods once.
            if (_peers.TryRemove(peer.PeerId, out AsyncPeer removedPeer))
            {
                // Close the socket on our end
                try
                {
                    removedPeer.Socket.Shutdown(SocketShutdown.Both);
                    removedPeer.Socket.Close();
                }
                catch
                {
                    // Do nothing
                }
                // Handler callback for peer disconnected
                try
                {
                    await _handler.PeerDisconnected(removedPeer).ConfigureAwait(false);
                }
                catch (Exception e)
                { await LogErrorAsync(e, PeerRemovedErrorMessage, false).ConfigureAwait(false); }
            }
        }

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;
            while (_serverRunning)
            {
                // Send Keep Alives every interval
                if (count == _keepAliveInterval)
                {
                    count = 0;
                    if (_peers.Count == 0)
                        continue;

                    async Task SendKeepAliveAsync(KeyValuePair<long, AsyncPeer> keyValuePair)
                    {
                        await keyValuePair.Value.SendKeepAlive().ConfigureAwait(false);
                    }

                    await _peers.ParallelForEachAsync(SendKeepAliveAsync, 24).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}