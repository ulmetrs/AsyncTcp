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
        private readonly Channel<ObjectPacket> _sendChannel;
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
            _sendChannel = Channel.CreateUnbounded<ObjectPacket>(new UnboundedChannelOptions() { SingleReader = true });
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
#if !NETCOREAPP3_1
                    bytesRead = await _socket.ReceiveAsync(memory.GetArray(), SocketFlags.None).ConfigureAwait(false);
#else
                    bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None).ConfigureAwait(false);
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

        // Pipelines optimized method of Parsing socket bytes
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ParseBytes(PipeReader reader)
        {
            ReadResult result;
            ReadOnlySequence<byte> buffer;
            object data;
            var packet = new DataPacket();

            while (true)
            {
                result = await reader.ReadAsync().ConfigureAwait(false);
                buffer = result.Buffer;

                while (TryParseBuffer(ref buffer, ref packet))
                {
                    if (packet.Length == 0)
                    {
                        // How much overhead does try-catch provide, would rather not blow up socket/server for a bad deserialization
                        try
                        {
                            await _handler.PacketReceived(this, packet.Type, null).ConfigureAwait(false);
                        }
                        catch { }

                        continue;
                    }

                    try
                    {
                        if (AsyncTcp.Config.StreamSerializer != null)
                        {
                            using (var inputStream = AsyncTcp.StreamManager.GetStream())
                            {
                                if (packet.Compressed)
                                {
                                    using (var compressionStream = new GZipStream(inputStream, CompressionMode.Decompress, true))
                                    {
                                        foreach (var segment in packet.Data)
                                        {
#if !NETCOREAPP3_1
                                            var array = segment.GetArray();
                                            await compressionStream.WriteAsync(array.Array, array.Offset, array.Count).ConfigureAwait(false);
#else
                                            await compressionStream.WriteAsync(segment).ConfigureAwait(false);
#endif
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (var segment in packet.Data)
                                    {
#if !NETCOREAPP3_1
                                        var array = segment.GetArray();
                                        await inputStream.WriteAsync(array.Array, array.Offset, array.Count).ConfigureAwait(false);
#else
                                        await inputStream.WriteAsync(segment).ConfigureAwait(false);
#endif
                                    }
                                }

                                data = AsyncTcp.Config.StreamSerializer.Deserialize(packet.Type, inputStream);
                            }
                        }
                        else
                        {
                            byte[] rented = null;

                            try
                            {
                                using (var outputStream = AsyncTcp.StreamManager.GetStream())
                                {
                                    foreach (var segment in packet.Data)
                                    {
#if !NETCOREAPP3_1
                                        var array = segment.GetArray();
                                        await outputStream.WriteAsync(array.Array, array.Offset, array.Count).ConfigureAwait(false);
#else
                                        await outputStream.WriteAsync(segment).ConfigureAwait(false);
#endif
                                    }

                                    var size = (int)outputStream.Length;
                                    rented = ArrayPool<byte>.Shared.Rent(size);

                                    outputStream.Position = 0;
                                    outputStream.Read(rented, 0, size);
                                }

                                var bytes = rented;

                                if (packet.Compressed)
                                {
                                    bytes = await Utils.DecompressWithGzipAsync(bytes).ConfigureAwait(false);
                                }

                                data = AsyncTcp.Config.ByteSerializer.Deserialize(packet.Type, bytes);
                            }
                            finally
                            {
                                if (rented != null)
                                    ArrayPool<byte>.Shared.Return(rented);
                            }

                            /* Much Simpler, but Heap Alloc
                            var bytes = packet.Data.ToArray();

                            if (packet.Compressed)
                            {
                                bytes = await Utils.DecompressWithGzipAsync(bytes).ConfigureAwait(false);
                            }

                            data = AsyncTcp.ByteSerializer.Deserialize(packet.Type, bytes);
                            */
                        }

                        await _handler.PacketReceived(this, packet.Type, data).ConfigureAwait(false);
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
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, ref DataPacket packet)
        {
            if (buffer.Length < AsyncTcp.HeaderSize)
            {
                return false;
            }

            var typeSlice = buffer.Slice(AsyncTcp.TypeOffset, AsyncTcp.IntSize);
            if (typeSlice.IsSingleSegment)
            {
                packet.Type = MemoryMarshal.Read<int>(typeSlice.First.Span);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[4];
                typeSlice.CopyTo(stackBuffer);
                packet.Type = MemoryMarshal.Read<int>(stackBuffer);
            }

            var lengthSlice = buffer.Slice(AsyncTcp.LengthOffset, AsyncTcp.IntSize);
            if (lengthSlice.IsSingleSegment)
            {
                packet.Length = MemoryMarshal.Read<int>(lengthSlice.First.Span);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[4];
                lengthSlice.CopyTo(stackBuffer);
                packet.Length = MemoryMarshal.Read<int>(stackBuffer);
            }

            packet.Compressed = MemoryMarshal.Read<bool>(buffer.Slice(AsyncTcp.CompressedOffset, AsyncTcp.BoolSize).First.Span);

            packet.Data = default;

            if (packet.Length == 0)
            {
                buffer = buffer.Slice(AsyncTcp.HeaderSize);
                return true;
            }

            if (AsyncTcp.HeaderSize + packet.Length > buffer.Length)
            {
                return false;
            }

            packet.Data = buffer.Slice(AsyncTcp.HeaderSize, packet.Length);
            buffer = buffer.Slice(AsyncTcp.HeaderSize + packet.Length);
            return true;
        }

        // Queue messages in the SendChannel for processing, swallow on error
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ProcessSend()
        {
            // Waits for a signal from the channel that data is ready to be read
            while (_alive && await _sendChannel.Reader.WaitToReadAsync(_sendCancel.Token).ConfigureAwait(false))
            {
                // Once signaled, continuously read data while it is available
                while (_alive && _sendChannel.Reader.TryRead(out var packet))
                {
                    await ProcessSend(packet).ConfigureAwait(false);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ProcessSend(ObjectPacket packet)
        {
            int bytesSent;

            // Header Byte messages are cached
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

                return;
            }

            if (AsyncTcp.Config.StreamSerializer != null)
            {
                using (var serializedStream = AsyncTcp.StreamManager.GetStream())
                {
                    // Serialize object into the managed stream
                    AsyncTcp.Config.StreamSerializer.Serialize(serializedStream, packet.Data);

                    // Reset position for next copy
                    serializedStream.Position = 0;

                    using (var sendStream = AsyncTcp.StreamManager.GetStream())
                    {
                        var useCompression = false;

                        // Move the send stream position past the header so we can write the payload first (we don't yet know the size)
                        sendStream.Position = AsyncTcp.HeaderSize;

                        // If compression is enabled, copy the compressed bytes
                        if (AsyncTcp.Config.UseCompression && serializedStream.Length >= AsyncTcp.CompressionCuttoff)
                        {
                            useCompression = true;

                            using (var compressionStream = new GZipStream(sendStream, CompressionMode.Compress, true))
                            {
                                await serializedStream.CopyToAsync(compressionStream).ConfigureAwait(false);
                            }
                        }
                        // Else copy the bytes
                        else
                        {
                            await serializedStream.CopyToAsync(sendStream).ConfigureAwait(false);
                        }

                        // Reset position to write header
                        sendStream.Position = 0;

                        var size = (int)sendStream.Length;
                        var length = size - AsyncTcp.HeaderSize;

                        using (var writer = new BinaryWriter(sendStream, System.Text.Encoding.UTF8, true))
                        {
                            writer.Write(packet.Type);
                            writer.Write(length);
                            writer.Write(useCompression);
                        }

                        // Grab the underlying stream buffer and write it to the socket
                        if (sendStream.TryGetBuffer(out var buffer))
                        {
                            try
                            {
                                bytesSent = 0;
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
            }
            else
            {
                var bytes = AsyncTcp.Config.ByteSerializer.Serialize(packet.Data);

                var useCompression = false;

                // If compression is enabled, copy the compressed bytes
                if (AsyncTcp.Config.UseCompression && bytes.Length >= AsyncTcp.CompressionCuttoff)
                {
                    useCompression = true;

                    bytes = await Utils.CompressWithGzipAsync(bytes).ConfigureAwait(false);
                }
                var length = bytes.Length;
                var size = length + AsyncTcp.HeaderSize;

                using (var outputStream = AsyncTcp.StreamManager.GetStream(null, size, true))
                {
                    using (var writer = new BinaryWriter(outputStream, System.Text.Encoding.UTF8, true))
                    {
                        writer.Write(packet.Type);
                        writer.Write(bytes.Length);
                        writer.Write(useCompression);
                        writer.Write(bytes);
                    }

                    // Grab the underlying stream buffer and write it to the socket
                    if (outputStream.TryGetBuffer(out var buffer))
                    {
                        try
                        {
                            bytesSent = 0;
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
                /*
                var length = bytes.Length;
                var size = length + AsyncTcp.HeaderSize;

                var buffer = ArrayPool<byte>.Shared.Rent(size);

                BitConverter.GetBytes(packet.Type).CopyTo(buffer, AsyncTcp.TypeOffset);
                BitConverter.GetBytes(length).CopyTo(buffer, AsyncTcp.LengthOffset);
                BitConverter.GetBytes(useCompression).CopyTo(buffer, AsyncTcp.CompressedOffset);
                bytes.CopyTo(buffer, AsyncTcp.HeaderSize);

                try
                {
                    bytesSent = 0;
                    while (bytesSent < size)
                    {
                        bytesSent += await _socket
                            .SendAsync(new ArraySegment<byte>(buffer, bytesSent, size - bytesSent), SocketFlags.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                */
            }

            // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
            if (packet.Type == AsyncTcp.Config.ErrorType)
            {
                ShutDown();
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

#if !NETCOREAPP3_1
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
#endif
}