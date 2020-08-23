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
        private readonly IAsyncHandler _handler;
        private readonly Channel<ObjectMessage> _sendChannel;
        private readonly CancellationTokenSource _sendCancel;

        private bool _alive = false;
        private int _keepAliveCount = 0;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket,
            IAsyncHandler handler)
        {
            _socket = socket;
            _handler = handler;
            _sendChannel = Channel.CreateUnbounded<ObjectMessage>(new UnboundedChannelOptions() { SingleReader = true });
            _sendCancel = new CancellationTokenSource();
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal async Task Process()
        {
            _alive = true;

            var pipe = AsyncTcp.Config.PipeOptions != null ? new Pipe(AsyncTcp.Config.PipeOptions) : new Pipe();

            // Start the 4 Running Processing Steps for a Peer
            var receiveTask = ReceiveFromSocket(pipe.Writer);
            var parseTask = ParseBytes(pipe.Reader);
            var sendTask = ProcessSend();
            var keepAliveTask = KeepAlive();

            await _handler.PeerConnected(this).ConfigureAwait(false);

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

            await _handler.PeerDisconnected(this).ConfigureAwait(false);
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
                memory = writer.GetMemory(AsyncTcp.MinReceiveBufferSize);

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
            ByteMessage message = new ByteMessage();

            while (true)
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
                buffer = result.Buffer;

                // Parse as many messages as we can from the buffer
                while (TryParseBuffer(ref buffer, ref message))
                {
                    // Handle Zero-Length Messages
                    if (message.Length == 0)
                    {
                        try
                        {
                            await _handler.MessageReceived(this, message.Type, null).ConfigureAwait(false);
                        }
                        catch { }

                        continue;
                    }

                    try
                    {
                        // If the message is compressed, we first need to Decompress the Bytes
                        if (message.Compressed)
                        {
                            // Unfortunately, cannot decompress directly from ReadOnlySequence, so fill the required input stream first
                            using (var inputStream = AsyncTcp.StreamManager.GetStream(null, message.Length))
                            {
                                foreach (var segment in message.Bytes)
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
                                    // If using the StreamSerializer, rent a stream and copy to it
                                    if (AsyncTcp.Config.StreamSerializer != null)
                                    {
                                        // Rent a managed stream with room for the decompressed bytes
                                        using (var outputStream = AsyncTcp.StreamManager.GetStream(null, message.DecompressedLength))
                                        {
                                            await compressionStream.CopyToAsync(outputStream).ConfigureAwait(false);

                                            // Reset stream position for the deserialize
                                            outputStream.Position = 0;

                                            // The one heap alloc we can't get away from
                                            var data = AsyncTcp.Config.StreamSerializer.Deserialize(message.Type, outputStream);

                                            await _handler.MessageReceived(this, message.Type, data).ConfigureAwait(false);
                                        }
                                    }
                                    // Else rent an array and read to it
                                    else
                                    {
                                        byte[] bytes = null;
                                        try
                                        {
                                            bytes = ArrayPool<byte>.Shared.Rent(message.DecompressedLength);
                                            await compressionStream.ReadAsync(bytes, 0, message.DecompressedLength);

                                            // The one heap alloc we can't get away from
                                            var data = AsyncTcp.Config.ByteSerializer.Deserialize(message.Type, bytes);

                                            await _handler.MessageReceived(this, message.Type, data).ConfigureAwait(false);
                                        }
                                        finally
                                        {
                                            ArrayPool<byte>.Shared.Return(bytes);
                                        }
                                    }
                                }
                            }
                        }
                        // If the message was not compressed, the APIs actually favor the byte serializer, since we can copy directly to a buffer
                        else
                        {
                            if (AsyncTcp.Config.ByteSerializer != null)
                            {
                                byte[] bytes = null;
                                try
                                {
                                    // Rent some bytes for the deserializer and copy directly from the message
                                    bytes = ArrayPool<byte>.Shared.Rent(message.Length);
                                    message.Bytes.CopyTo(bytes);

                                    // The one heap alloc we can't get away from
                                    var data = AsyncTcp.Config.ByteSerializer.Deserialize(message.Type, bytes);

                                    await _handler.MessageReceived(this, message.Type, data).ConfigureAwait(false);
                                }
                                finally
                                {
                                    ArrayPool<byte>.Shared.Return(bytes);
                                }
                            }
                            else
                            {
                                using (var outputStream = AsyncTcp.StreamManager.GetStream(null, message.Length))
                                {
                                    foreach (var segment in message.Bytes)
                                    {
#if NETCOREAPP3_1
                                        await outputStream.WriteAsync(segment).ConfigureAwait(false);
#else
                                        var array = segment.GetArray();
                                        await outputStream.WriteAsync(array.Array, array.Offset, array.Count).ConfigureAwait(false);
#endif
                                    }

                                    // Reset stream position for the deserialize
                                    outputStream.Position = 0;

                                    // The one heap alloc we can't get away from
                                    var data = AsyncTcp.Config.StreamSerializer.Deserialize(message.Type, outputStream);

                                    await _handler.MessageReceived(this, message.Type, data).ConfigureAwait(false);
                                }
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
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, ref ByteMessage message)
        {
            if (buffer.Length < 9)
            {
                return false;
            }

            if (!message.ParsedHeader)
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
                    message.Length = MemoryMarshal.Read<int>(lengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    lengthSlice.CopyTo(stackBuffer);
                    message.Length = MemoryMarshal.Read<int>(stackBuffer);
                }

                message.Compressed = false;
                message.Bytes = default;

                if (message.Length == 0)
                {
                    message.ParsedHeader = false; // Reset parse header flag
                    buffer = buffer.Slice(9);
                    return true;
                }
            }

            if (9 + message.Length > buffer.Length)
            {
                message.ParsedHeader = true; // Set this flag so we can skip parsing the header next time
                return false;
            }

            message.ParsedHeader = false; // Reset parse header flag
            message.Compressed = MemoryMarshal.Read<bool>(buffer.Slice(8, 1).First.Span);
            message.Bytes = buffer.Slice(9, message.Length);

            if (message.Compressed)
            {
                var decompressedLengthSlice = buffer.Slice(5 + message.Length, 4);
                if (decompressedLengthSlice.IsSingleSegment)
                {
                    message.DecompressedLength = MemoryMarshal.Read<int>(decompressedLengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    decompressedLengthSlice.CopyTo(stackBuffer);
                    message.DecompressedLength = MemoryMarshal.Read<int>(stackBuffer);
                }
            }

            buffer = buffer.Slice(9 + message.Length);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(int type)
        {
            return Send(AsyncTcp.HeaderMessages(type));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(int type, object data)
        {
            return Send(new ObjectMessage() { Type = type, Data = data });
        }

        // Queue messages in the SendChannel for processing, swallow on error
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task Send(ObjectMessage message)
        {
            try
            {
                await _sendChannel
                   .Writer
                   .WriteAsync(message, _sendCancel.Token)
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
                while (_alive && _sendChannel.Reader.TryRead(out var message))
                {
                    await ProcessSend(message).ConfigureAwait(false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ProcessSend(ObjectMessage message)
        {
            // Send Zero-Length Messages, we cache the bytes for these messages since they will always be the same 9 byte regions
            if (message.Data == null)
            {
                try
                {
                    int bytesSent = 0;
                    while (bytesSent < 9)
                    {
                        bytesSent += await _socket
                            .SendAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(message.Type), bytesSent, 9 - bytesSent), SocketFlags.None)
                            .ConfigureAwait(false);
                    }
                }
                catch { }

                return;
            }

            if (AsyncTcp.Config.StreamSerializer != null)
            {
                using (var serializedStream = AsyncTcp.StreamManager.GetStream())
                {
                    // Serialize object into the managed stream
                    AsyncTcp.Config.StreamSerializer.Serialize(serializedStream, message.Data);

                    // Reset stream position for next copy
                    serializedStream.Position = 0;

                    await CompressAndSend(serializedStream, message.Type).ConfigureAwait(false);
                }
            }
            else
            {
                var bytes = AsyncTcp.Config.ByteSerializer.Serialize(message.Data);

                using (var serializedStream = AsyncTcp.StreamManager.GetStream(null, bytes.Length))
                {
                    serializedStream.Write(bytes, 0, bytes.Length);
                    // Reset stream position for next copy
                    serializedStream.Position = 0;

                    await CompressAndSend(serializedStream, message.Type).ConfigureAwait(false);
                }
            }

            // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
            if (message.Type == AsyncTcp.Config.ErrorType)
            {
                ShutDown();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task CompressAndSend(Stream inputStream, int type)
        {
            using (var outputStream = AsyncTcp.StreamManager.GetStream())
            {
                var useCompression = false;

                // Move the send stream position past the header so we can write the payload first (we don't yet know the size)
                outputStream.Position = 9;

                // If compression is enabled, copy the compressed bytes
                if (AsyncTcp.Config.UseCompression && inputStream.Length >= AsyncTcp.CompressionCuttoff)
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

                // Reset position to write header
                outputStream.Position = 0;
                WriteHeader(outputStream, type, size - 9, useCompression);

                // Grab the underlying stream buffer and write it to the socket
                // Would be excellent if there was ReadOnlySequence overloads for the RecyclableStream AND Socket API, but there isn't
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
        private void WriteHeader(Stream stream, int type, int length, bool compressed)
        {
            Span<byte> stackBuffer = stackalloc byte[9];
            var typeSlice = stackBuffer.Slice(0, 4);
            MemoryMarshal.Write(typeSlice, ref type);
            var lengthSlice = stackBuffer.Slice(4, 4);
            MemoryMarshal.Write(lengthSlice, ref length);
            var compressedSlice = stackBuffer.Slice(8, 1);
            MemoryMarshal.Write(compressedSlice, ref compressed);
#if NETCOREAPP3_1
            stream.Write(stackBuffer);
#else
            stream.Write(stackBuffer.ToArray(), 0, 9);
#endif
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

                    await Send(AsyncTcp.Config.KeepAliveType).ConfigureAwait(false);
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