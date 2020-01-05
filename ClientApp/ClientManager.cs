using AsyncTcp;
using AsyncTest;
using System;
using System.Threading.Tasks;

namespace ClientApp
{
    public class ClientManager : IAsyncHandler
    {
        public AsyncClient AsyncClient { get; }
        public string ServerHostName { get; }

        public ClientManager(string serverHostName)
        {
            ServerHostName = serverHostName;
            AsyncClient = new AsyncClient(this, 1024, 10);
        }

        public Task Start()
        {
            return AsyncClient.Start(ServerHostName, 9050, true);
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

        public async Task DataReceived(AsyncPeer peer, int dataType, byte[] data)
        {
            var bytes = await AsyncTcp.Utils.DecompressWithGzipAsync(data).ConfigureAwait(false);
            var message = Utf8Json.JsonSerializer.Deserialize<TestMessage>(bytes);

            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);
        }
    }
}
