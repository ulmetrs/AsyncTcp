using AsyncTcp;
using System;
using System.Threading.Tasks;

namespace AsyncTester
{
    public class ClientManager : IAsyncHandler
    {
        public AsyncClient _asyncClient { get; }
        public string ServerHostName { get; }
        private Task StartTask;

        public ClientManager(string serverHostName)
        {
            ServerHostName = serverHostName;
            _asyncClient = new AsyncClient(this, 1024, 10);
        }

        public void Start()
        {
            StartTask = _asyncClient.Start(ServerHostName);
        }

        public Task Shutdown()
        {
            _asyncClient.Stop();
            return Task.CompletedTask;
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        public async Task DataReceived(AsyncPeer peer, int dataType, int dataSize, byte[] data)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) data received...").ConfigureAwait(false);
        }
    }
}
