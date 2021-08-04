using AsyncTcp;
using AsyncTest;
using System;
using System.Buffers;
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
            AsyncTcp.AsyncTcp.Initialize(null, this);
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

        public Task PeerConnected(AsyncPeer peer)
        {
            return Task.CompletedTask;
        }

        public Task PeerDisconnected(AsyncPeer peer)
        {
            return Task.CompletedTask;
        }

        public Task ReceiveUnreliable(AsyncPeer peer, ReadOnlyMemory<byte> buffer)
        {
            var waypoint = MemoryMarshal.Read<Waypoint>(buffer.Span);
            return Task.CompletedTask;
        }

        public Task Receive(AsyncPeer peer, int type, ReadOnlySequence<byte> buffer)
        {
            return Task.CompletedTask;
            /*
                if (type == AsyncTcp.AsyncTcp.KeepAliveType)
                    return;

                // Handle Zero-Length Messages
                if (buffer.Length == 0)
                {
                    await HandleMessage(peer, type, null).ConfigureAwait(false);
                    return;
                }

                using (var stream = StreamManager.GetStream(null, (int)buffer.Length))
                {
                    foreach (var segment in buffer)
                    {
                        await stream.WriteAsync(segment).ConfigureAwait(false);
                    }

                    stream.Position = 0;

                    var compressed = Convert.ToBoolean(stream.ReadByte());

                    // Stupid Utf8Json Wont Serialize a Stream with an offset, so we need to make this either way
                    using (var outputStream = StreamManager.GetStream())
                    {
                        if (compressed)
                        {
                            using (var compressionStream = new GZipStream(stream, CompressionMode.Decompress))
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
                            await stream.CopyToAsync(outputStream).ConfigureAwait(false);

                            // Reset stream position for the deserialize
                            outputStream.Position = 0;

                            // Handle the stream
                            await HandleMessage(peer, type, outputStream).ConfigureAwait(false);
                        }
                    }
                }
            */
        }
        }
    }