using AsyncTcp;
using System;
using System.Threading.Tasks;

namespace AsyncTester
{
    public class ServerManager : IAsyncHandler
    {
        public AsyncServer AsyncServer { get; }
        private Task StartTask;

        public ServerManager()
        {
            AsyncServer = new AsyncServer(this, 1024, 10);
        }

        public void Start()
        {
            StartTask = AsyncServer.Start();
        }

        public Task Shutdown()
        {
            AsyncServer.Stop();
            return Task.CompletedTask;
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        public async Task DataReceived(AsyncPeer peer, int dataType, int dataSize, byte[] data)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) data received...").ConfigureAwait(false);
        }
    }
}
