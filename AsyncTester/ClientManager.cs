using AsyncTcp;
using System;
using System.Text;
using System.Threading.Tasks;

namespace AsyncTester
{
    public class ClientManager : IAsyncHandler
    {
        public AsyncClient AsyncClient { get; }
        public string ServerHostName { get; }
        private Task StartTask;

        public ClientManager(string serverHostName)
        {
            ServerHostName = serverHostName;
            AsyncClient = new AsyncClient(this, 1024, 10);
        }

        public void Start()
        {
            StartTask = AsyncClient.Start(ServerHostName, 9050, true);
        }

        public void Shutdown()
        {
            AsyncClient.Stop();
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
            var dataString = data == null ? "null" : Encoding.UTF8.GetString(data);
            await Console.Out.WriteLineAsync($"Data: {dataString}").ConfigureAwait(false);
        }
    }
}
