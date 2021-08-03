using AsyncTcp;
using AsyncTest;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
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

        public Task HandleUDPPacket(AsyncPeer peer, Memory<byte> buffer)
        {
            var waypoint = MemoryMarshal.Read<Waypoint>(buffer.Span);
            //_ = HandleWaypoint(peer, waypoint); // TODO make value task?
            return Task.CompletedTask;
        }

        // Simple implementation of packing the stream however you want from the Message you Queued to send
        // Custom bit packing along with pooled object can provide the optimal performance for this
        public async Task PackMessage(AsyncPeer peer, int type, object payload, Stream packToStream)
        {
            using (var inputStream = new MemoryStream())
            {
                // Serialize object into the managed stream
                Utf8Json.JsonSerializer.Serialize(inputStream, payload);

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
        public Task DisposeMessage(AsyncPeer peer, int type, object payload)
        {
            return Task.CompletedTask; 
        }

        // Simple implementation of unpacking the stream into whatever usable format you want
        // Custom bit packing along with pooled object can provide the optimal performance for this
        public async Task UnpackMessage(AsyncPeer peer, int type, Stream unpackFromStream)
        {
            // Handle Zero-Length Messages
            if (unpackFromStream == null)
            {
                await HandleMessage(peer, type, unpackFromStream).ConfigureAwait(false);
                return;
            }

            var compressed = Convert.ToBoolean(unpackFromStream.ReadByte());

            // Stupid Utf8Json Wont Serialize a Stream with an offset, so we need to make this either way
            using (var outputStream = new MemoryStream())
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

                    // Reset stream position for the deserialize
                    outputStream.Position = 0;

                    // Handle the stream
                    await HandleMessage(peer, type, outputStream).ConfigureAwait(false);
                }
            }
        }

        private class Test
        {
            public string Name { get; set; }
            public int Type { get; set; }
        }

        private class Error
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
                    index = i,
                    data = _xorShifter.GetRandomBytes(5000),
                };
                await client.Send(1, letter).ConfigureAwait(false);
                await Task.Delay(_random.Next(1, 3) * 1000);
            }
        }
    }
}