using AsyncTcpBytes;
using AsyncTest;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;

namespace ClientApp
{
    public class ClientScenarios : IPeerHandler
    {
        private IPAddress _address;
        private XorShift _xorShifter;
        private Random _random;

        public ClientScenarios(IPAddress address)
        {
            var config = new Config()
            {
                PeerHandler = this
            };
            AsyncTcp.Configure(config);

            _address = address;
            _xorShifter = new XorShift(true);
            _random = new Random();
        }

        public async Task PeerConnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) connected...").ConfigureAwait(false);
        }

        public async Task PeerDisconnected(AsyncPeer peer)
        {
            await Console.Out.WriteLineAsync($"Client (PeerId: {peer.PeerId}) disconnected...").ConfigureAwait(false);
        }

        // Simple implementation of packing the stream however you want from the Message you Queued to send
        // Custom bit packing along with pooled object can provide the optimal performance for this
        public async Task PackMessage(AsyncPeer peer, Message message, Stream packToStream)
        {
            using (var inputStream = AsyncTcp.StreamManager.GetStream())
            {
                // Serialize object into the managed stream
                // Stupid Utf8Json needs Breaks when this is not cast to object
                Utf8Json.JsonSerializer.Serialize(inputStream, (object)message);

                // Reset stream position for next copy
                inputStream.Position = 0;

                // If UseCompression
                if (inputStream.Length >= 860)
                {
                    packToStream.WriteByte(Convert.ToByte(true));
                    using (var compressionStream = new GZipStream(packToStream, CompressionMode.Compress, true))
                    {
                        await inputStream.CopyToAsync(compressionStream).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Stupid Utf8Json won't serialize from an offset, so we need to do this double copy
                    packToStream.WriteByte(Convert.ToByte(false));
                    await inputStream.CopyToAsync(packToStream).ConfigureAwait(false);
                }
            }
        }

        // Implement this to dispose of the message after a send if you are pooling objects
        public Task DisposeMessage(AsyncPeer peer, Message message)
        {
            return Task.CompletedTask; 
        }

        // Simple implementation of unpacking the stream into whatever usable format you want
        // Custom bit packing along with pooled object can provide the optimal performance for this
        public async Task UnpackMessage(AsyncPeer peer, int type, Stream unpackFromStream)
        {
            var compressed = Convert.ToBoolean(unpackFromStream.ReadByte());

            // Stupid Utf8Json Wont Serialize a Stream with an offset, so we need to make this either way
            using (var outputStream = AsyncTcp.StreamManager.GetStream())
            {
                if (compressed)
                {
                    using (var compressionStream = new GZipStream(unpackFromStream, CompressionMode.Decompress))
                    {
                        await compressionStream.CopyToAsync(outputStream).ConfigureAwait(false);

                        // Reset stream position for the deserialize
                        outputStream.Position = 0;

                        // Handle the decompressed stream
                        await HandleMessage(peer, type, outputStream).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Stupid Utf8Json Wont Serialize a Stream with an offset, so we need to make this either way
                    await unpackFromStream.CopyToAsync(outputStream).ConfigureAwait(false);
                    // Handle the stream
                    await HandleMessage(peer, type, outputStream).ConfigureAwait(false);
                }
            }
        }

        private class Test : Message
        {
            public string Name { get; set; }
            public int Type { get; set; }
        }

        private class Error : Message
        {
            public string Err { get; set; }
        }

        private async Task HandleMessage(AsyncPeer peer, int type, Stream stream)
        {
            try
            {
                switch (type)
                {
                    case 1:
                        var test = Utf8Json.JsonSerializer.Deserialize<Test>(stream);
                        await DoTestStuff(test).ConfigureAwait(false);
                        break;
                    default:
                        var error = Utf8Json.JsonSerializer.Deserialize<Error>(stream);
                        Console.WriteLine("Error Message : " + error.Err);
                        break;
                }
            }
            catch { }
        }

        private Task DoTestStuff(Test test)
        {
            return Task.CompletedTask;
        }

        public async Task RunKeepAliveScenarioAsync(int clientCount)
        {
            await Console.Out.WriteLineAsync($"Client creation count is {clientCount}.").ConfigureAwait(false);

            var clients = new List<AsyncClient>();
            var tasks = new List<Task>();

            for (int i = 0; i < clientCount; i++)
            {
                var client = new AsyncClient();
                clients.Add(client);
                tasks.Add(client.Start(_address));
            }

            await Console.Out.WriteLineAsync($"Successfully Added Clients. Count: {clients.Count}.").ConfigureAwait(false);

            if (clients.Count > 0)
                await Task.Delay(1000 * 60).ConfigureAwait(false);

            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].ShutDown();
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
                var client = new AsyncClient();
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
                clients[i].ShutDown();
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
                    MessageType = 1,
                    index = i,
                    data = _xorShifter.GetRandomBytes(5000),
                };
                await client.Peer.Send(letter).ConfigureAwait(false);
                await Task.Delay(_random.Next(1, 3) * 1000);
            }
        }
    }
}