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

        private readonly Socket _socket;
        private readonly IAsyncHandler _handler;
        private readonly Channel<ObjectPacket> _sendChannel;
        private readonly CancellationTokenSource _sendCancel;

        private bool _alive;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket,
            IAsyncHandler handler)
        {
            _socket = socket;
            _handler = handler;
            _sendChannel = Channel.CreateUnbounded<ObjectPacket>(new UnboundedChannelOptions() { SingleReader = true });
            _sendCancel = new CancellationTokenSource();
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        internal async Task Process()
        {
            _alive = true;

            // There are many pipe options we can play with
            //var options = new PipeOptions(pauseWriterThreshold: 10, resumeWriterThreshold: 5);
            var pipe = new Pipe();

            // Start the 4 Running Processing Steps for a Peer
            var receiveTask = Task.Run(() => ReceiveFromSocket(pipe.Writer));
            var parseTask = Task.Run(() => ParseBytes(pipe.Reader));
            var sendTask = Task.Run(() => ProcessSend());
            var keepAliveTask = Task.Run(() => KeepAlive());

            await _handler.PeerConnected(this).ConfigureAwait(false);

            // Wait for sockets to close and parsing to finish
            await Task.WhenAll(receiveTask, parseTask).ConfigureAwait(false);

            // This prevents us from re-entering the 'WaitToReadAsync' loop after a shutdown call, but
            // does not break us out.  Its possible to re-enter the 'WaitToReadAsync' AFTER the
            // TryComplete and CancellationTokens are invoked, we need to cover both to avoid getting
            // the process stuck
            _alive = false;
            // We force close the channels, and in most scenarios the tryComplete successfully breaks
            // us out of the 'WaitToReadAsync' call on the channel's reader so the task can complete
            _sendChannel.Writer.TryComplete();
            // IOS would not break us out of the 'WaitToReadAsync' but this did
            _sendCancel.Cancel();

            // Wait for the remaining tasks to finish
            await Task.WhenAll(sendTask, keepAliveTask).ConfigureAwait(false);

            await _handler.PeerDisconnected(this).ConfigureAwait(false);
        }

        // Shutdown processing at any point via error, via error message, or manually
        public void ShutDown()
        {
            // Idiosynchrosies for socket class throw exceptions in various scenarios,
            // I just want it ShutDown and Closed at all costs
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }

        // Queue messages in the SendChannel for processing, swallow on error
        public async Task Send(int type, object data = null)
        {
            try
            {
                await _sendChannel
                   .Writer
                   .WriteAsync(new ObjectPacket() { Type = type, Data = data }, _sendCancel.Token)
                   .ConfigureAwait(false);
            }
            catch { }
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
                        break;
                    }
                    writer.Advance(bytesRead);
                }
                catch
                {
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
            object data;
            var packet = new BytePacket();

            while (true)
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
                buffer = result.Buffer;

                while (TryParseBuffer(ref buffer, ref packet))
                {
                    data = null;

                    if (packet.Length > 0)
                    {
                        try
                        {
                            if (packet.Compressed)
                            {
                                packet.Bytes = await DecompressWithGzipAsync(packet.Bytes).ConfigureAwait(false);
                            }

                            data = AsyncTcp.Serializer.Deserialize(packet.Type, packet.Bytes);
                        }
                        catch (Exception e)
                        {
                            await _handler.PacketReceived(this, -3, "2. PARSE EXCEPTION: " + e).ConfigureAwait(false);
                        }
                    }

                    try { await _handler.PacketReceived(this, packet.Type, data).ConfigureAwait(false); } catch { }
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
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, ref BytePacket packet)
        {
            if (buffer.Length < AsyncTcp.HeaderSize)
            {
                return false;
            }

            packet.Type = BitConverter.ToInt32(buffer.Slice(AsyncTcp.TypeOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);
            packet.Length = BitConverter.ToInt32(buffer.Slice(AsyncTcp.LengthOffset, AsyncTcp.IntSize).ToArray(), AsyncTcp.ZeroOffset);
            packet.Compressed = BitConverter.ToBoolean(buffer.Slice(AsyncTcp.CompressedOffset, AsyncTcp.BoolSize).ToArray(), AsyncTcp.ZeroOffset);
            packet.Bytes = null;

            if (packet.Length == 0)
            {
                buffer = buffer.Slice(AsyncTcp.HeaderSize, buffer.End);
                return true;
            }

            if (AsyncTcp.HeaderSize + packet.Length > buffer.Length)
            {
                return false;
            }

            packet.Bytes = buffer.Slice(AsyncTcp.HeaderSize, packet.Length).ToArray();
            buffer = buffer.Slice(AsyncTcp.HeaderSize + packet.Length);
            return true;
        }

        // Processes the SendChannel Queue of messages and sends them through the socket
        private async Task ProcessSend()
        {
            bool useCompression;
            byte[] bytes;
            int size;
            byte[] buffer;
            int bytesSent;

            // Waits for a signal from the channel that data is ready to be read
            while (_alive && await _sendChannel.Reader.WaitToReadAsync(_sendCancel.Token).ConfigureAwait(false))
            {
                // Once signaled, continuously read data while it is available
                while (_sendChannel.Reader.TryRead(out var packet))
                {
                    // Header-only messages are optimized since we cache the HeaderBytes and don't need to rebuild them
                    if (packet.Data == null)
                    {
                        try
                        {
                            bytesSent = 0;
                            while (bytesSent < AsyncTcp.HeaderSize)
                            {
                                bytesSent += await _socket
                                    .SendAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(packet.Type), bytesSent, AsyncTcp.HeaderSize - bytesSent), SocketFlags.None)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        useCompression = false;
                        bytes = AsyncTcp.Serializer.Serialize(packet.Data);

                        if (AsyncTcp.UseCompression && bytes.Length >= AsyncTcp.CompressionCuttoff)
                        {
                            useCompression = true;
                            bytes = await CompressWithGzipAsync(bytes).ConfigureAwait(false);
                        }

                        size = AsyncTcp.HeaderSize + bytes.Length;
                        buffer = ArrayPool<byte>.Shared.Rent(size);

                        // TODO there should be more efficient memory-methods of copying int and bool values to the array
                        BitConverter.GetBytes(packet.Type).CopyTo(buffer, AsyncTcp.TypeOffset);
                        BitConverter.GetBytes(bytes.Length).CopyTo(buffer, AsyncTcp.LengthOffset);
                        BitConverter.GetBytes(useCompression).CopyTo(buffer, AsyncTcp.CompressedOffset);
                        bytes.CopyTo(buffer, AsyncTcp.HeaderSize);

                        bytesSent = 0;
                        try
                        {
                            while (bytesSent < size)
                            {
                                bytesSent += await _socket
                                    .SendAsync(new ArraySegment<byte>(buffer, bytesSent, size - bytesSent), SocketFlags.None)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch { }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
                    if (packet.Type == AsyncTcp.ErrorType)
                    {
                        ShutDown();
                    }
                }
            }
        }

        private async Task KeepAlive()
        {
            var count = AsyncTcp.KeepAliveInterval;

            while (_alive)
            {
                if (count == AsyncTcp.KeepAliveInterval)
                {
                    count = 0;
                    await Send(AsyncTcp.KeepAliveType).ConfigureAwait(false);
                }
                else
                {
                    count++;
                }

                await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);
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