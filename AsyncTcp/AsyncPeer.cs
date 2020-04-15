using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

        private bool _shutdown;
        private Channel<ObjectPacket> _sendChannel;
        private Channel<BytePacket> _receiveChannel;
        private CancellationTokenSource _channelCancel;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket,
            IAsyncHandler handler )
        {
            _socket = socket;
            _handler = handler;
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        internal async Task Process()
        {
            _shutdown = false;
            _sendChannel = Channel.CreateUnbounded<ObjectPacket>(new UnboundedChannelOptions() { AllowSynchronousContinuations = true, SingleReader = true });
            _receiveChannel = Channel.CreateUnbounded<BytePacket>(new UnboundedChannelOptions() { AllowSynchronousContinuations = true, SingleReader = true, SingleWriter = true });
            _channelCancel = new CancellationTokenSource();

            // There are many pipe options we can play with
            //var options = new PipeOptions(pauseWriterThreshold: 10, resumeWriterThreshold: 5);
            var pipe = new Pipe();

            // Start the 4 Running Processing Steps for a Peer
            var sendTask = ProcessSend();
            var receiveTask = ReceiveFromSocket(pipe.Writer);
            var parseTask = ParseBytes(pipe.Reader);
            var processTask = ProcessPacket();

            try { await _handler.PeerConnected(this).ConfigureAwait(false); } catch { }

            await Task.WhenAll(sendTask, receiveTask, parseTask, processTask).ConfigureAwait(false);

            try { await _handler.PeerDisconnected(this).ConfigureAwait(false); } catch { }
        }

        // Shutdown processing at any point via error, via error message, or manually
        public void ShutDown()
        {
            // This prevents us from re-entering the 'WaitToReadAsync' loop after a shutdown call, but
            // does not break us out.  Its possible to re-enter the 'WaitToReadAsync' AFTER the
            // TryComplete and CancellationTokens are invoked, we need to cover both to avoid getting
            // the process stuck
            _shutdown = true;
            // We force close the channels, and in most scenarios the tryComplete successfully breaks
            // us out of the 'WaitToReadAsync' call on the channel's reader so the task can complete
            _sendChannel.Writer.TryComplete();
            _receiveChannel.Writer.TryComplete();
            // IOS would not break us out of the 'WaitToReadAsync' but this did
            _channelCancel.Cancel();
            // Idiosynchrosies for socket class throw exceptions in various scenarios,
            // I just want it ShutDown and Closed at all costs
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }

        // Queue messages in the SendChannel for processing, swallow on error
        public async Task Send(int type, object data = null)
        {
            // Channel can close at any time
            try
            {
                await _sendChannel
                   .Writer
                   .WriteAsync(new ObjectPacket() { Type = type, Data = data }, _channelCancel.Token)
                   .ConfigureAwait(false);
            }
            catch
            { }
        }

        // Processes the SendChannel Queue of messages and sends them through the socket
        private async Task ProcessSend()
        {
            // Waits for a signal from the channel that data is ready to be read
            while (!_shutdown && await _sendChannel.Reader.WaitToReadAsync(_channelCancel.Token).ConfigureAwait(false))
            {
                // Once signaled, continuously read data while it is available
                while (_sendChannel.Reader.TryRead(out var packet))
                {
                    // Header-only messages are optimized since we cache the HeaderBytes and don't need to rebuild them
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
                        // TODO there should be more efficient memory-methods of copying int and bool values to the array
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

                    // If the packet type is of ErrorType this indicates that we want to immediately Shutdown the peer
                    // after the message is sent
                    if (packet.Type == AsyncTcp.ErrorType)
                    {
                        ShutDown();
                        break;
                    }
                }
            }
        }

        // Pipelines optimized method of Receiving socket bytes
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
                    bytesRead = await _socket.ReceiveAsync(memory.GetArray(), SocketFlags.None).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        ShutDown();
                        break;
                    }
                    writer.Advance(bytesRead);
                }
                catch
                {
                    ShutDown();
                    break;
                }

                result = await writer.FlushAsync().ConfigureAwait(false);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await writer.CompleteAsync().ConfigureAwait(false);
        }

        // Pipelines optimized method of Parsing socket bytes
        // We immediately Queue messages into the Receive Channel for Asynchronous processing
        // as we don't want the serialization/compression/handler callback to block us from
        // reading sequential messages from the socket
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

                    // Channel can close at any time
                    try
                    {
                        await _receiveChannel
                            .Writer
                            .WriteAsync(new BytePacket() { Type = packet.Item1, Compressed = packet.Item2, Bytes = packet.Item3 }, _channelCancel.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    { }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await reader.CompleteAsync().ConfigureAwait(false);
        }

        // We use a type-length strategy of demarcating messages, this probably would be faster if we had
        // more optimized int/bool parsing from the slice and not 'BitConverter'
        // Another strategy is the Special character 'EOL' way of splitting messages
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

        // Processes the ReceiveChannel Queue of messages and Sends them to the Handler's PacketReceived callback
        private async Task ProcessPacket()
        {
            while (!_shutdown && await _receiveChannel.Reader.WaitToReadAsync(_channelCancel.Token).ConfigureAwait(false))
            {
                while (_receiveChannel.Reader.TryRead(out var packet))
                {
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
                        catch { }
                    }

                    try { await _handler.PacketReceived(this, packet.Type, data).ConfigureAwait(false); } catch  { }
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