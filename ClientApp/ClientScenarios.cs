using AsyncTcp;
using AsyncTest;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace ClientApp
{
    public class ClientScenarios : IAsyncHandler, ISerializer
    {
        private IPAddress _address;
        private XorShift _xorShifter;
        private Random _random;

        public ClientScenarios(IPAddress address)
        {
            AsyncTcp.AsyncTcp.Initialize(this);
            _address = address;
            _xorShifter = new XorShift(true);
            _random = new Random();
        }

        public async Task RunKeepAliveScenarioAsync(int clientCount)
        {
            await Console.Out.WriteLineAsync($"Client creation count is {clientCount}.").ConfigureAwait(false);

            var clients = new List<AsyncClient>();
            var tasks = new List<Task>();

            for (int i = 0; i < clientCount; i++)
            {
                var client = new AsyncClient(this);
                clients.Add(client);
                tasks.Add(client.Start(_address));
            }

            await Console.Out.WriteLineAsync($"Successfully Added Clients. Count: {clients.Count}.").ConfigureAwait(false);

            if (clients.Count > 0)
                await Task.Delay(1000 * 60).ConfigureAwait(false);

            for (int i = 0; i < clients.Count; i++)
            {
                await clients[i].ShutDown().ConfigureAwait(false);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            await Console.Out.WriteLineAsync("Finished KeepAliveScenario").ConfigureAwait(false);
        }

        public async Task RunDataScenarioAsync(int clientCount)
        {
            var clients = new List<AsyncClient>();
            var tasks = new List<Task>();

            for (int i = 0; i < clientCount; i++)
            {
                var client = new AsyncClient(this);
                clients.Add(client);
                tasks.Add(client.Start(_address));
            }

            await Task.Delay(10000).ConfigureAwait(false);

            var sendTasks = new List<Task>();
            for (int i = 0; i < clients.Count; i++)
            {
                sendTasks.Add(DataTask(clients[i]));
            }

            await Task.WhenAll(sendTasks).ConfigureAwait(false);

            await Console.Out.WriteLineAsync("Finished SendTasks").ConfigureAwait(false);

            for (int i = 0; i < clients.Count; i++)
            {
                await clients[i].ShutDown().ConfigureAwait(false);
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            await Console.Out.WriteLineAsync("Finished DataScenario").ConfigureAwait(false);
        }

        private async Task DataTask(AsyncClient client)
        {
            for (int i = 0; i < 1000; i++)
            {
                var letter = new TestMessage()
                {
                    index = i,
                    data = _xorShifter.GetRandomBytes(5000),
                };
                await client.Send(1, letter).ConfigureAwait(false);
                await Task.Delay(_random.Next(1, 3) * 1000);
            }
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
            if (type == 1)
            {
                var message = (TestMessage)packet;

                await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) Received Message: {message.index}").ConfigureAwait(false);
            }
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