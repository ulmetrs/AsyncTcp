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
            AsyncServer = new AsyncServer(this, 10);
        }

        public Task Start()
        {
            return AsyncServer.Start();
        }

        public void Shutdown()
        {
            AsyncServer.ShutDown();
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        public async Task PacketReceived(AsyncPeer peer, int type, object packet)
        {
            var message = (TestMessage)packet;

            await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);

            await peer.Send(type, message);
        }
    }
}