using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static AsyncTcp.Utils;

namespace AsyncTcp
{
    public class AsyncPeer
    {
        private static long GlobalPeerId = -1;
        public long PeerId { get; } = Interlocked.Increment(ref GlobalPeerId);

        private Socket _socket;
        private IAsyncHandler _handler;
        private Channel<ChannelPacket> _sendChannel;
        private Channel<ChannelPacket> _receiveChannel;
        private bool _processing;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket,
            IAsyncHandler handler )
        {
            _socket = socket;
            _handler = handler;
            _sendChannel = Channel.CreateUnbounded<ChannelPacket>();
            _receiveChannel = Channel.CreateUnbounded<ChannelPacket>();
        }

        internal async Task Process()
        {
            _processing = true;
            var sendTask = Task.Run(ProcessSend);
            var receiveTask = Task.Run(ProcessReceive);
            var packetTask = Task.Run(ProcessPacket);
            await Task.WhenAll(sendTask, receiveTask, packetTask).ConfigureAwait(false);
            // TODO how to dispose of channels?
            _processing = false;
        }

        public void ShutDown()
        {
            _processing = false;
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            catch { }
        }

        public async Task Send(int type)
        {
            if (AsyncTcp.HeaderBytes.ContainsKey(type))
            {
                await _sendChannel
                    .Writer
                    .WriteAsync(new ChannelPacket() { Type = type, Data = null })
                    .ConfigureAwait(false);
            }
            else
            {
                await Logging
                    .LogMessageAsync("Cannot Send Packet, Type not Initialized")
                    .ConfigureAwait(false);
            }
        }

        public async Task Send(int type, object data)
        {
            if (AsyncTcp.HeaderBytes.ContainsKey(type))
            {
                await _sendChannel
                    .Writer
                    .WriteAsync(new ChannelPacket() { Type = type, Data = data })
                    .ConfigureAwait(false);
            }
            else
            {
                await Logging
                    .LogMessageAsync("Cannot Send Packet, Type not Initialized")
                    .ConfigureAwait(false);
            }
        }

        private async Task ProcessSend()
        {
            using (var netStream = new NetworkStream(_socket))
            {
                while (_processing)
                {
                    if (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                    {
                        var packet = await _sendChannel.Reader.ReadAsync().ConfigureAwait(false);

                        if (packet.Data == null)
                        {
                            try
                            {
                                await netStream
                                    .WriteAsync(AsyncTcp.HeaderBytes[packet.Type], AsyncTcp.TypeOffset, AsyncTcp.HeaderSize)
                                    .ConfigureAwait(false);
                            }
                            catch
                            {
                                ShutDown();
                                break;
                            }
                        }
                        else
                        {
                            var bytes = AsyncTcp.Serialize(packet.Data);

                            if (AsyncTcp.UseCompression)
                            {
                                bytes = await CompressWithGzipAsync(bytes).ConfigureAwait(false);
                            }

                            var size = AsyncTcp.HeaderSize + bytes.Length;
                            var buffer = ArrayPool<byte>.Shared.Rent(size);
                            BitConverter.GetBytes(packet.Type).CopyTo(buffer, AsyncTcp.TypeOffset);
                            BitConverter.GetBytes(bytes.Length).CopyTo(buffer, AsyncTcp.LengthOffset);
                            bytes.CopyTo(buffer, AsyncTcp.HeaderSize);

                            try
                            {
                                await netStream
                                    .WriteAsync(buffer, AsyncTcp.TypeOffset, size)
                                    .ConfigureAwait(false);
                            }
                            catch
                            {
                                ShutDown();
                                break;
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }
                        }
                    }
                }
            }
        }

        private async Task ProcessReceive()
        {
            using (var netStream = new NetworkStream(_socket))
            {
                var pipeReader = PipeReader.Create(netStream);
                while (_processing)
                {
                    // FIXME How do we tell when socket has closed, is it exception, is it result.IsCancelled/Completed, or is it buffer.Lenth == 0?
                    ReadResult result;
                    try
                    {
                        result = await pipeReader
                            .ReadAsync()
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        ShutDown();
                        break;
                    }
                    if (result.IsCanceled || result.IsCompleted)
                    {
                        ShutDown();
                        break;
                    }

                    var buffer = result.Buffer;

                    while (true)
                    {
                        if (buffer.Length < AsyncTcp.HeaderSize)
                        {
                            // FIXME do we need to advance in this scenario or can we just break?
                            pipeReader.AdvanceTo(buffer.Start, buffer.End);
                            break;
                        }

                        var type = BitConverter.ToInt32(buffer.Slice(AsyncTcp.TypeOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);
                        var length = BitConverter.ToInt32(buffer.Slice(AsyncTcp.LengthOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);

                        if (length == 0)
                        {
                            await _receiveChannel
                                .Writer
                                .WriteAsync(new ChannelPacket() { Type = type, Data = null })
                                .ConfigureAwait(false);

                            buffer = buffer.Slice(AsyncTcp.HeaderSize, buffer.End);
                            pipeReader.AdvanceTo(buffer.Start, buffer.End);
                            continue;
                        }

                        var size = AsyncTcp.HeaderSize + length;
                        if (size > buffer.Length)
                        {
                            // FIXME do we need to advance in this scenario or can we just break?
                            pipeReader.AdvanceTo(buffer.Start, buffer.End);
                            break;
                        }

                        var bytes = buffer.Slice(AsyncTcp.HeaderSize, length).ToArray();

                        if (AsyncTcp.UseCompression)
                        {
                            bytes = await DecompressWithGzipAsync(bytes).ConfigureAwait(false);
                        }

                        var data = AsyncTcp.Deserialize(type, bytes);

                        await _receiveChannel
                            .Writer
                            .WriteAsync(new ChannelPacket() { Type = type, Data = data })
                            .ConfigureAwait(false);

                        buffer = buffer.Slice(size);
                        pipeReader.AdvanceTo(buffer.Start, buffer.End);
                    }
                }
            }
        }

        private async Task ProcessPacket()
        {
            while (_processing)
            {
                if (await _receiveChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    var packet = await _receiveChannel.Reader.ReadAsync().ConfigureAwait(false);

                    try
                    {
                        await _handler.PacketReceived(this, packet.Type, packet.Data).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        await Logging
                            .LogErrorAsync(e, "Error Processing Packet")
                            .ConfigureAwait(false);
                    }
                }
            }
        }
    }
}