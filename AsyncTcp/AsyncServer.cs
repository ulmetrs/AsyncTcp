using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Logging;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncServer
    {
        private readonly IAsyncHandler _handler;
        private readonly int _keepAliveInterval;
        private readonly int _taskCleanupInterval = 100;

        private IPAddress _ipAddress;
        private int _bindPort;

        private Socket _listener;
        private bool _serverRunning;
        private readonly ConcurrentDictionary<long, AsyncPeer> _peers = new ConcurrentDictionary<long, AsyncPeer>();

        public string ServerHostName { get; private set; }

        public AsyncServer(
            IAsyncHandler handler,
            int keepAliveInterval = 10)
        {
            if (!AsyncTcp.Initialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
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
                    // Break on first IPv4 address.
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

            _listener = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(_ipAddress, _bindPort));
            _listener.Listen(100);

            _serverRunning = true;
            var tasks = new List<Task>
            {
                Task.Run(KeepAlive)
            };

            int taskCount = 0;
            Socket socket;
            try
            {
                // Accept all connections while server running
                while (_serverRunning && (socket = await _listener.AcceptAsync().ConfigureAwait(false)) != null)
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
            { }

            // Wait for all remaining tasks to finish
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ProcessSocket(Socket socket)
        {
            var peer = new AsyncPeer(socket, _handler);
            _peers[peer.PeerId] = peer;

            try
            {
                await _handler.PeerConnected(peer).ConfigureAwait(false);
            }
            catch (Exception e)
            { await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false); }

            await peer.Process().ConfigureAwait(false);

            await RemovePeer(peer).ConfigureAwait(false);
        }

        public void ShutDown()
        {
            // Turn off server running flag
            _serverRunning = false;

            // Send Kill Signal to the Listener Socket
            try
            {
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();
            }
            catch
            { }

            if (_peers.Count == 0)
                return;

            // Send Kill Signals to the Peer Sockets
            foreach (var kvp in _peers)
            {
                kvp.Value.ShutDown();
            }
        }

        public async Task RemovePeer(AsyncPeer peer)
        {
            // RemovePeer can be called from the server directly: upon closing the socket ReceiveAsync will exit appropriately and call RemovePeer
            // again. With this check we only only call the handler methods once.
            if (_peers.TryRemove(peer.PeerId, out AsyncPeer removedPeer))
            {
                // Close the socket on our end
                removedPeer.ShutDown();

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
                        await keyValuePair.Value.Send(0).ConfigureAwait(false);
                    }

                    await _peers.ParallelForEachAsync(SendKeepAliveAsync, 24).ConfigureAwait(false);
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