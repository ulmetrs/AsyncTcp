using AsyncTcp;
using AsyncTest;
using System;
using System.Net;
using System.Threading.Tasks;

namespace ServerApp
{
    public class ServerScenarios : ISerializer, IAsyncHandler
    {
        public ServerScenarios()
        {
            AsyncTcp.AsyncTcp.Initialize(this);
        }

        public async Task RunServer()
        {
            var server = new AsyncServer(this);
            var task = Task.Run(() => Shutdown(server));
            await server.Start(IPAddress.Loopback).ConfigureAwait(false);
        }

        public async Task RemovePeer(AsyncServer server)
        {
            await Task.Delay(10000).ConfigureAwait(false);
            await server.RemovePeer(0).ConfigureAwait(false);
        }

        public async Task Shutdown(AsyncServer server)
        {
            await Task.Delay(10000).ConfigureAwait(false);
            await server.ShutDown().ConfigureAwait(false);
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
            if (type == 1)
            {
                var message = (TestMessage)packet;

                await Console.Out.WriteLineAsync($"Server (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);

                await peer.Send(type, message);
            }
        }
    }
}