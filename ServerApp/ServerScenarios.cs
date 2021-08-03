using AsyncTcp;
using AsyncTest;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ServerApp
{
    public class ServerScenarios : IPeerHandler
    {
        public ServerScenarios()
        {
            
        }

        public Task HandleUDPPacket(AsyncPeer peer, Memory<byte> buffer)
        {
            var waypoint = MemoryMarshal.Read<Waypoint>(buffer.Span);
            //_ = HandleWaypoint(peer, waypoint); // TODO make value task?
            return Task.CompletedTask;
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

        public async Task RunServer()
        {
            var server = new AsyncServer();
            var task = Task.Run(() => Shutdown(server));
            await server.Start(IPAddress.Loopback).ConfigureAwait(false);
        }

        public async Task Shutdown(AsyncServer server)
        {
            await Task.Delay(10000).ConfigureAwait(false);
            server.ShutDown();
        }

        public Task PackUnreliableMessage(AsyncPeer peer, int type, object payload, Stream packToStream)
        {
            throw new NotImplementedException();
        }

        public Task UnpackUnreliableMessage(AsyncPeer peer, int type, Stream unpackFromStream)
        {
            throw new NotImplementedException();
        }
    }
}