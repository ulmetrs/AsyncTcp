using AsyncTcp;
using AsyncTest;
using System;
using System.Threading.Tasks;

namespace ClientApp
{
    public class ClientManager : IAsyncHandler, ISerializer
    {
        public AsyncClient AsyncClient { get; }
        public string ServerHostName { get; }

        public ClientManager(string serverHostName)
        {
            ServerHostName = serverHostName;
            AsyncTcp.AsyncTcp.Initialize(this);
            AsyncClient = new AsyncClient(this);
        }

        public Task Start()
        {
            return AsyncClient.Start(ServerHostName, 9050, true);
        }

        public Task Shutdown()
        {
            return AsyncClient.ShutDown();
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        public async Task PacketReceived(AsyncPeer peer, int type, object packet)
        {
            var message = (TestMessage)packet;

            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);
        }

        public byte[] Serialize(object obj)
        {
            return Utf8Json.JsonSerializer.Serialize(obj);
        }

        public object Deserialize(int type, byte[] bytes)
        {
            if (type == 1)
            {
                return Utf8Json.JsonSerializer.Deserialize<TestMessage>(bytes);
            }
            return null;
        }
    }
}