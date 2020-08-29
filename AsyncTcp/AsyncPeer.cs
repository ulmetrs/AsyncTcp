using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncPeer
    {
        private static long GlobalPeerId = -1;
        public long PeerId { get; } = Interlocked.Increment(ref GlobalPeerId);

        private readonly Socket _socket;
        private readonly Pipe _receivePipe;
        private readonly Channel<(int, object)> _sendChannel;
        private readonly CancellationTokenSource _sendCancel;

        private bool _alive;
        private int _keepAliveCount;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket)
        {
            _socket = socket;
            _receivePipe = new Pipe(AsyncTcp.ReceivePipeOptions);
            _sendChannel = Channel.CreateUnbounded<(int, object)>(new UnboundedChannelOptions() { SingleReader = true });
            _sendCancel = new CancellationTokenSource();
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal async Task Process()
        {
            _alive = true;

            // Start the 4 Running Processing Steps for a Peer
            var receiveTask = ReceiveFromSocket(_receivePipe.Writer);
            var parseTask = ParseBytes(_receivePipe.Reader);
            var sendTask = ProcessSend();
            var keepAliveTask = KeepAlive();

            await AsyncTcp.PeerHandler.PeerConnected(this).ConfigureAwait(false);

            // Wait for sockets to close and parsing to finish
            await receiveTask.ConfigureAwait(false);
            await parseTask.ConfigureAwait(false);

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
            await sendTask.ConfigureAwait(false);
            await keepAliveTask.ConfigureAwait(false);

            await AsyncTcp.PeerHandler.PeerDisconnected(this).ConfigureAwait(false);
        }

        // Shutdown called manually or via send error
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ShutDown()
        {
            // Idiosynchrosies for socket class throw exceptions in various scenarios,
            // I just want it ShutDown and Closed at all costs
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }

        // Pipelines optimized method of Receiving socket bytes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ReceiveFromSocket(PipeWriter writer)
        {
            Memory<byte> memory;
            int bytesRead;
            FlushResult result;

            while (true)
            {
                memory = writer.GetMemory(AsyncTcp.ReceiveBufferSize);

                try
                {
#if NETCOREAPP3_1
                    bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None).ConfigureAwait(false);
#else
                    bytesRead = await _socket.ReceiveAsync(memory.GetArray(), SocketFlags.None).ConfigureAwait(false);
#endif
                    Interlocked.Exchange(ref _keepAliveCount, 0);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ParseBytes(PipeReader reader)
        {
            ReadResult result;
            ReadOnlySequence<byte> buffer;
            ParseInfo info = new ParseInfo();

            while (true)
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
                buffer = result.Buffer;

                // Parse as many messages as we can from the buffer
                while (TryParseBuffer(ref buffer, ref info))
                {
                    // Handle Zero-Length Messages
                    if (info.Size == 0)
                    {
                        try
                        {
                            await AsyncTcp.PeerHandler.MessageReceived(this, info.Type, null).ConfigureAwait(false);
                        }
                        catch { }

                        continue;
                    }

                    try
                    {
                        // If the message is compressed, we first need to Decompress the Bytes
                        if (info.Compressed)
                        {
                            // Unfortunately, cannot decompress directly from ReadOnlySequence, so fill the required input stream first
                            using (var inputStream = AsyncTcp.StreamManager.GetStream(null, info.Size))
                            {
                                foreach (var segment in info.Buffer)
                                {
#if NETCOREAPP3_1
                                    await inputStream.WriteAsync(segment).ConfigureAwait(false);
#else
                                    var array = segment.GetArray();
                                    await inputStream.WriteAsync(array.Array, array.Offset, array.Count).ConfigureAwait(false);
#endif
                                }

                                // Reset input stream for compression
                                inputStream.Position = 0;

                                // Now to decompress
                                using (var compressionStream = new GZipStream(inputStream, CompressionMode.Decompress))
                                {
                                    var bytes = ArrayPool<byte>.Shared.Rent(info.DecompressedSize);
                                    try
                                    {
                                        await compressionStream.ReadAsync(bytes, 0, info.DecompressedSize);

                                        var data = AsyncTcp.Serializer.Deserialize(info.Type, info.DecompressedSize, bytes);

                                        await AsyncTcp.PeerHandler.MessageReceived(this, info.Type, data).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        ArrayPool<byte>.Shared.Return(bytes);
                                    }
                                }
                            }
                        }
                        else
                        {
                            var bytes = ArrayPool<byte>.Shared.Rent(info.Size);
                            try
                            {
                                info.Buffer.CopyTo(bytes);

                                var data = AsyncTcp.Serializer.Deserialize(info.Type, info.Size, bytes);

                                await AsyncTcp.PeerHandler.MessageReceived(this, info.Type, data).ConfigureAwait(false);
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(bytes);
                            }
                        }
                    }
                    catch { }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            await reader.CompleteAsync().ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, ref ParseInfo message)
        {
            if (buffer.Length < 9)
            {
                return false;
            }

            if (!message.HeaderParsed)
            {
                var typeSlice = buffer.Slice(0, 4);
                if (typeSlice.IsSingleSegment)
                {
                    message.Type = MemoryMarshal.Read<int>(typeSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    typeSlice.CopyTo(stackBuffer);
                    message.Type = MemoryMarshal.Read<int>(stackBuffer);
                }

                var lengthSlice = buffer.Slice(4, 4);
                if (lengthSlice.IsSingleSegment)
                {
                    message.Size = MemoryMarshal.Read<int>(lengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    lengthSlice.CopyTo(stackBuffer);
                    message.Size = MemoryMarshal.Read<int>(stackBuffer);
                }

                message.Compressed = false;
                message.Buffer = default;

                if (message.Size == 0)
                {
                    message.HeaderParsed = false; // Reset parse header flag
                    buffer = buffer.Slice(9);
                    return true;
                }
            }

            if (9 + message.Size > buffer.Length)
            {
                message.HeaderParsed = true; // Set this flag so we can skip parsing the header next time
                return false;
            }

            message.HeaderParsed = false; // Reset parse header flag
            message.Compressed = MemoryMarshal.Read<bool>(buffer.Slice(8, 1).First.Span);
            message.Buffer = buffer.Slice(9, message.Size);

            if (message.Compressed)
            {
                var decompressedLengthSlice = buffer.Slice(5 + message.Size, 4);
                if (decompressedLengthSlice.IsSingleSegment)
                {
                    message.DecompressedSize = MemoryMarshal.Read<int>(decompressedLengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    decompressedLengthSlice.CopyTo(stackBuffer);
                    message.DecompressedSize = MemoryMarshal.Read<int>(stackBuffer);
                }
            }

            buffer = buffer.Slice(9 + message.Size);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task Send(int type)
        {
            try
            {
                await _sendChannel
                   .Writer
                   .WriteAsync((type, null), _sendCancel.Token)
                   .ConfigureAwait(false);
            }
            catch { }
        }

        // Messages are assumed to be rented from the Message Pool Manager
        // If ManualReturn() resolves to false (default) the message will be returned to the pool
        // Ensure IMessage AND MemoryPoolManager are implemented correctly, there are no
        // Guard rails here
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task Send(int type, object payload)
        {
            try
            {
                await _sendChannel
                   .Writer
                   .WriteAsync((type, payload), _sendCancel.Token)
                   .ConfigureAwait(false);
            }
            catch { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ProcessSend()
        {
            // Waits for a signal from the channel that data is ready to be read
            while (_alive && await _sendChannel.Reader.WaitToReadAsync(_sendCancel.Token).ConfigureAwait(false))
            {
                // Once signaled, continuously read data while it is available
                while (_alive && _sendChannel.Reader.TryRead(out (int, object) message))
                {
                    await ProcessSend(message.Item1, message.Item2).ConfigureAwait(false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ProcessSend(int type, object payload)
        {
            // Send Zero-Length Messages, we cache the bytes for these messages since they will always be the same 9 byte regions
            if (payload == null)
            {
                try
                {
                    int bytesSent = 0;
                    while (bytesSent < 9)
                    {
                        bytesSent += await _socket
                            .SendAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(type), bytesSent, 9 - bytesSent), SocketFlags.None)
                            .ConfigureAwait(false);
                    }
                }
                catch { }

                return;
            }

            var bytes = AsyncTcp.Serializer.Serialize(type, payload);

            using (var inputStream = AsyncTcp.StreamManager.GetStream(null, bytes.Length))
            {
                await inputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);

                // Reset stream position for next copy
                inputStream.Position = 0;

                await CompressAndSend(type, inputStream).ConfigureAwait(false);
            }

            // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
            if (type == AsyncTcp.ErrorType)
            {
                ShutDown();
            }
        }

        private readonly byte[] header = new byte[9];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task CompressAndSend(int type, Stream inputStream)
        {
            using (var outputStream = AsyncTcp.StreamManager.GetStream())
            {
                var useCompression = false;

                // Move the send stream position past the header so we can write the payload first (we don't yet know the size)
                outputStream.Position = 9;

                // If compression is enabled, copy the compressed bytes
                if (AsyncTcp.UseCompression && inputStream.Length >= AsyncTcp.CompressionCutoff)
                {
                    useCompression = true;

                    using (var compressionStream = new GZipStream(outputStream, CompressionMode.Compress, true))
                    {
                        await inputStream.CopyToAsync(compressionStream).ConfigureAwait(false);
                    }
                }
                // Else copy the bytes
                else
                {
                    await inputStream.CopyToAsync(outputStream).ConfigureAwait(false);
                }

                var size = (int)outputStream.Length;

                // Go back and write the header now that we know the size
                outputStream.Position = 0;
                WriteInt(header, 0, type);
                WriteInt(header, 4, size - 8);
                header[9] = Convert.ToByte(useCompression);
                outputStream.Write(header, 0, 9);

                outputStream.Position = 0;
                if (outputStream.TryGetBuffer(out var buffer))
                {
                    try
                    {
                        int bytesSent = 0;
                        while (bytesSent < size)
                        {
                            bytesSent += await _socket
                                .SendAsync(new ArraySegment<byte>(buffer.Array, buffer.Offset + bytesSent, size - bytesSent), SocketFlags.None)
                                .ConfigureAwait(false);
                        }
                    }
                    catch { }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteInt(byte[] buffer, int offset, int value)
        {
            for (int i = 0, j = 0; i < 4; i++, j++)
            {
                buffer[offset + i] = (byte)(value >> (8 * j));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task KeepAlive()
        {
            var count = AsyncTcp.KeepAliveInterval;

            await Task.Delay(AsyncTcp.KeepAliveDelay).ConfigureAwait(false);

            while (_alive)
            {
                if (count == AsyncTcp.KeepAliveInterval)
                {
                    count = 0;

                    Interlocked.Increment(ref _keepAliveCount);

                    // If we have counted at least 3 keepAlives and we havn't received any assume the connection is closed
                    if (_alive && _keepAliveCount >= 3)
                    {
                        ShutDown();
                        break;
                    }

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