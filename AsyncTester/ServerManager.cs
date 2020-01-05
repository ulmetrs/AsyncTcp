using AsyncTcp;
using System;
using System.Text;
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
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) data received...").ConfigureAwait(false);

            var dataString = data == null ? "null" : Encoding.UTF8.GetString(data);
            await Console.Out.WriteLineAsync($"Data: {dataString}").ConfigureAwait(false);
        }
    }
}
