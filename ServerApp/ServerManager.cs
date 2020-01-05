using AsyncTcp;
using AsyncTest;
using System;
using System.Threading.Tasks;

namespace ServerApp
{
    public class ServerManager : IAsyncHandler
    {
        public AsyncServer AsyncServer { get; }

        public ServerManager()
        {
            AsyncServer = new AsyncServer(this, 1024, 10);
        }

        public Task Start()
        {
            return AsyncServer.Start();
        }

        public void Shutdown()
        {
            AsyncServer.Stop();
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        public async Task DataReceived(AsyncPeer peer, int dataType, byte[] data)
        {
            var bytes = await AsyncTcp.Utils.DecompressWithGzipAsync(data).ConfigureAwait(false);
            var message = Utf8Json.JsonSerializer.Deserialize<TestMessage>(bytes);

            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);

            await peer.SendAsync(dataType, data);
        }
    }
}