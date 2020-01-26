using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using static AsyncTcp.Logging;
using static AsyncTcp.Utils;

namespace AsyncTcp
{
    public class AsyncPeer
    {
        private static long GlobalPeerId = -1;
        public long PeerId { get; } = Interlocked.Increment(ref GlobalPeerId);

        private Socket _socket;
        private IAsyncHandler _handler;
        private Channel<ObjectPacket> _sendChannel;
        private Channel<BytePacket> _receiveChannel;
        private bool _processing;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket,
            IAsyncHandler handler )
        {
            _socket = socket;
            _handler = handler;
        }

        internal async Task Process()
        {
            _sendChannel = Channel.CreateUnbounded<ObjectPacket>();
            _receiveChannel = Channel.CreateUnbounded<BytePacket>();

            // There are many pipe options we can play with
            //var options = new PipeOptions(pauseWriterThreshold: 10, resumeWriterThreshold: 5);
            var pipe = new Pipe();
            var sendTask = ProcessSend();
            var receiveTask = ReceiveFromSocket(pipe.Writer);
            var parseTask = ParseBytes(pipe.Reader);
            var processTask = ProcessPacket();

            _processing = true;

            try
            {
                await _handler.PeerConnected(this).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, PeerConnectedErrorMessage, false).ConfigureAwait(false);
            }

            await Task.WhenAll(sendTask, receiveTask, parseTask, processTask).ConfigureAwait(false);

            try
            {
                await _handler.PeerDisconnected(this).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await LogErrorAsync(e, PeerRemovedErrorMessage, false).ConfigureAwait(false);
            }
        }

        private void ShutDown()
        {
            _processing = false;

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Close();
            }
            catch { }

            _sendChannel.Writer.TryComplete();
            _receiveChannel.Writer.TryComplete();
        }

        public async Task Send(int type, object data = null)
        {
            if (_processing)
            {
                await _sendChannel
                    .Writer
                    .WriteAsync(new ObjectPacket() { Type = type, Data = data })
                    .ConfigureAwait(false);
            }
        }

        private async Task ProcessSend()
        {
            while (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var packet = await _sendChannel.Reader.ReadAsync().ConfigureAwait(false);

                if (packet.Data == null)
                {
                    int bytesSent = 0;
                    try
                    {
                        while (bytesSent < AsyncTcp.HeaderSize)
                        {
                            bytesSent += await _socket
                                .SendAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(packet.Type), bytesSent, AsyncTcp.HeaderSize - bytesSent), SocketFlags.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        ShutDown();
                        break;
                    }
                }
                else
                {
                    var useCompression = false;
                    var bytes = AsyncTcp.Serializer.Serialize(packet.Data);

                    if (AsyncTcp.UseCompression && bytes.Length >= AsyncTcp.CompressionCuttoff)
                    {
                        useCompression = true;
                        bytes = await CompressWithGzipAsync(bytes).ConfigureAwait(false);
                    }

                    var size = AsyncTcp.HeaderSize + bytes.Length;
                    var buffer = ArrayPool<byte>.Shared.Rent(size);
                    BitConverter.GetBytes(packet.Type).CopyTo(buffer, AsyncTcp.TypeOffset);
                    BitConverter.GetBytes(bytes.Length).CopyTo(buffer, AsyncTcp.LengthOffset);
                    BitConverter.GetBytes(useCompression).CopyTo(buffer, AsyncTcp.CompressedOffset);
                    bytes.CopyTo(buffer, AsyncTcp.HeaderSize);

                    int bytesSent = 0;
                    try
                    {
                        while (bytesSent < size)
                        {
                            bytesSent += await _socket
                                .SendAsync(new ArraySegment<byte>(buffer, bytesSent, size - bytesSent), SocketFlags.None)
                                .ConfigureAwait(false);
                        }
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

                // We send an error from the server so that the client can retrieve an error reason, but we don't want to wait for the client to shutdown
                if (packet.Type == AsyncTcp.ErrorType)
                {
                    ShutDown();
                    break;
                }
            }
        }

        private async Task ReceiveFromSocket(PipeWriter writer)
        {
            Memory<byte> memory;
            int bytesRead;
            FlushResult result;

            while (true)
            {
                memory = writer.GetMemory(AsyncTcp.MinReceiveBufferSize);

                try
                {
                    bytesRead = await _socket.ReceiveAsync(memory.GetArray(), SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        ShutDown();
                        break;
                    }
                    writer.Advance(bytesRead);
                }
                catch (Exception ex)
                {
                    ShutDown();
                    await LogErrorAsync(ex, ReceiveErrorMessage, false).ConfigureAwait(false);
                    break;
                }

                result = await writer.FlushAsync();

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await writer.CompleteAsync().ConfigureAwait(false);
        }

        private async Task ParseBytes(PipeReader reader)
        {
            ReadResult result;
            ReadOnlySequence<byte> buffer;

            while (true)
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
                buffer = result.Buffer;

                while (TryParseBuffer(ref buffer, out Tuple<int, bool, byte[]> packet))
                {
                    if (packet.Item1 == AsyncTcp.KeepAliveType)
                        continue;

                    await _receiveChannel
                        .Writer
                        .WriteAsync(new BytePacket() { Type = packet.Item1, Compressed = packet.Item2, Bytes = packet.Item3 })
                        .ConfigureAwait(false);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await reader.CompleteAsync().ConfigureAwait(false);
        }

        // Honestly, a Delimiter character might be worth using, that way we can grab the entire sequence, parse out the header from the slice and do the same for the buffer
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, out Tuple<int, bool, byte[]> packet)
        {
            if (buffer.Length < AsyncTcp.HeaderSize)
            {
                packet = default;
                return false;
            }

            var type = BitConverter.ToInt32(buffer.Slice(AsyncTcp.TypeOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);
            var length = BitConverter.ToInt32(buffer.Slice(AsyncTcp.LengthOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);
            var compressed = BitConverter.ToBoolean(buffer.Slice(AsyncTcp.CompressedOffset, AsyncTcp.BoolSize).ToArray(), AsyncTcp.ZeroOffset);

            if (length == 0)
            {
                packet = new Tuple<int, bool, byte[]>(type, compressed, null);
                buffer = buffer.Slice(AsyncTcp.HeaderSize, buffer.End);
                return true;
            }

            var size = AsyncTcp.HeaderSize + length;

            if (size > buffer.Length)
            {
                packet = default;
                return false;
            }

            packet = new Tuple<int, bool, byte[]>(type, compressed, buffer.Slice(AsyncTcp.HeaderSize, length).ToArray());
            buffer = buffer.Slice(size);
            return true;
        }

        private async Task ProcessPacket()
        {
            while (await _receiveChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                var packet = await _receiveChannel.Reader.ReadAsync().ConfigureAwait(false);

                var bytes = packet.Bytes;

                object data = null;

                if (bytes != null)
                {
                    try
                    {
                        if (packet.Compressed)
                        {
                            bytes = await DecompressWithGzipAsync(bytes).ConfigureAwait(false);
                        }

                        data = AsyncTcp.Serializer.Deserialize(packet.Type, bytes);
                    }
                    catch (Exception e)
                    {
                        await LogErrorAsync(e, ReceiveErrorMessage, false).ConfigureAwait(false);
                    }
                }

                try
                {
                    await _handler.PacketReceived(this, packet.Type, data).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    await LogErrorAsync(e, ReceiveErrorMessage, false).ConfigureAwait(false);
                }

                if (packet.Type == AsyncTcp.ErrorType)
                {
                    ShutDown();
                    break;
                }
            }
        }
    }

    public static class BufferExtensions
    {
        public static ArraySegment<byte> GetArray(this Memory<byte> memory)
        {
            return ((ReadOnlyMemory<byte>)memory).GetArray();
        }

        public static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> memory)
        {
            if (!MemoryMarshal.TryGetArray(memory, out var result))
            {
                throw new InvalidOperationException("Buffer backed by array was expected");
            }
            return result;
        }
    }
}