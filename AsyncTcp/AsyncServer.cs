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
        private readonly ConcurrentDictionary<long, AsyncPeer> _peers;

        private Socket _listener;
        private bool _serverRunning;

        public string HostName { get; private set; }

        public AsyncServer(
            IAsyncHandler handler,
            int keepAliveInterval = AsyncTcp.KeepAliveInterval)
        {
            if (!AsyncTcp.IsInitialized)
                throw new Exception("AsyncTcp must be initialized before creating a client");

            _handler = handler ?? throw new Exception("Handler cannot be null");
            _keepAliveInterval = keepAliveInterval;
            _peers = new ConcurrentDictionary<long, AsyncPeer>();
        }

        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (_serverRunning)
                throw new Exception("Cannot Start, Server is running");

            try
            {
                if (address == null)
                {
                    address = await Utils.GetIPAddress().ConfigureAwait(false);
                }

                HostName = address.ToString();

                _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                _listener.NoDelay = true;
                _listener.Bind(new IPEndPoint(address, bindPort));
                _listener.Listen(100);
            }
            catch
            {
                throw;
            }

            await LogMessageAsync(string.Format(HostnameMessage, HostName, address, bindPort), false).ConfigureAwait(false);

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
                while ((socket = await _listener.AcceptAsync().ConfigureAwait(false)) != null)
                {
                    socket.NoDelay = true;
                    // Add Async Task Process Socket, this task will handle the new connection until it closes
                    tasks.Add(ProcessSocket(socket));
                    // Increment task count so we know when to run our task cleanup again
                    taskCount++;
                    // Cleanup our tasks list, server can be long running so we don't wan't to append tasks forever
                    if (taskCount >= AsyncTcp.TaskCleanupInterval)
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
            catch(Exception e)
            {
                await LogErrorAsync(e, "Accepted Loop Exception", false).ConfigureAwait(false);
            }

            await ShutDown().ConfigureAwait(false);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task ProcessSocket(Socket socket)
        {
            var peer = new AsyncPeer(socket, _handler);

            _peers[peer.PeerId] = peer;

            await peer.Process().ConfigureAwait(false);

            await RemovePeer(peer).ConfigureAwait(false);
        }

        public async Task ShutDown(object data = null)
        {
            _serverRunning = false;

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
            async Task SendErrorAsync(KeyValuePair<long, AsyncPeer> kv)
            {
                await kv.Value.Send(AsyncTcp.ErrorType, data).ConfigureAwait(false);
            }

            await _peers.ParallelForEachAsync(SendErrorAsync, 24).ConfigureAwait(false);
        }

        public Task RemovePeer(AsyncPeer peer, object data = null)
        {
            return RemovePeer(peer.PeerId, data);
        }

        public async Task RemovePeer(long peerId, object data = null)
        {
            if (_peers.TryRemove(peerId, out AsyncPeer peer))
            {
                await peer.Send(AsyncTcp.ErrorType, data).ConfigureAwait(false);
            }
        }

        private async Task KeepAlive()
        {
            var count = _keepAliveInterval;

            while (_serverRunning)
            {
                await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);

                if (count == _keepAliveInterval)
                {
                    count = 0;

                    if (_peers.Count == 0)
                    {
                        continue;
                    }

                    async Task SendKeepAliveAsync(KeyValuePair<long, AsyncPeer> kv)
                    {
                        await kv.Value.Send(AsyncTcp.KeepAliveType).ConfigureAwait(false);
                    }

                    await _peers.ParallelForEachAsync(SendKeepAliveAsync, 24).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }
            }
        }
    }
}