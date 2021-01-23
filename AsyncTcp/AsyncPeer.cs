using System;
using System.Buffers;
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
        private readonly Channel<(int, object)> _sendChannel;
        private readonly CancellationTokenSource _sendCancel;

        private bool _alive;
        private int _keepAliveCount;

        // Peers are constructed from connected sockets
        internal AsyncPeer(
            Socket socket)
        {
            _socket = socket;
            _sendChannel = Channel.CreateUnbounded<(int, object)>(new UnboundedChannelOptions() { SingleReader = true });
            _sendCancel = new CancellationTokenSource();
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal async Task Process()
        {
            _alive = true;

            // Start the 3 Running Processing Steps for a Peer
            var receiveTask = ProcessReceive();
            var sendTask = ProcessSend();
            var keepAliveTask = KeepAlive();

            await AsyncTcp.PeerHandler.PeerConnected(this).ConfigureAwait(false);

            // Wait for sockets to close and parsing to finish
            await receiveTask.ConfigureAwait(false);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task ProcessReceive()
        {
            var netStream = new NetworkStream(_socket);
            var reader = PipeReader.Create(netStream, AsyncTcp.ReceivePipeOptions);
            var info = new ParseInfo();

            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    // Parse as many messages as we can from the buffer
                    while (TryParseBuffer(ref buffer, info))
                    {
                        // Handle Keep Alive Check
                        if (info.Type == AsyncTcp.KeepAliveType)
                        {
                            // Reset out KeepAliveCount to indicate we have received some data from the client
                            Interlocked.Exchange(ref _keepAliveCount, 0);
                        }

                        await AsyncTcp.PeerHandler.HandleMessage(this, info.Type, info.Size, info.Size == 0 ? null : info.Buffer.ToArray()).ConfigureAwait(false);
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch
            {
                ShutDown();
            }

            await reader.CompleteAsync().ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryParseBuffer(ref ReadOnlySequence<byte> buffer, ParseInfo info)
        {
            if (buffer.Length < 8)
            {
                return false;
            }

            if (!info.HeaderParsed)
            {
                var typeSlice = buffer.Slice(0, 4);
                if (typeSlice.IsSingleSegment)
                {
                    info.Type = MemoryMarshal.Read<int>(typeSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    typeSlice.CopyTo(stackBuffer);
                    info.Type = MemoryMarshal.Read<int>(stackBuffer);
                }

                var sizeSlice = buffer.Slice(4, 4);
                if (sizeSlice.IsSingleSegment)
                {
                    info.Size = MemoryMarshal.Read<int>(sizeSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    sizeSlice.CopyTo(stackBuffer);
                    info.Size = MemoryMarshal.Read<int>(stackBuffer);
                }

                if (info.Size == 0)
                {
                    info.HeaderParsed = false; // Reset parse header flag
                    buffer = buffer.Slice(8);
                    return true;
                }
            }

            if (8 + info.Size > buffer.Length)
            {
                info.HeaderParsed = true; // Set this flag so we can skip parsing the header next time
                return false;
            }

            info.HeaderParsed = false; // Reset parse header flag
            info.Buffer = buffer.Slice(8, info.Size);
            buffer = buffer.Slice(8 + info.Size);
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
            try
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
            catch { }
        }

        private readonly byte[] header = new byte[8];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task ProcessSend(int type, object payload)
        {
            if (payload == null)
            {
                try
                {
                    int bytesSent = 0;
                    while (bytesSent < 8)
                    {
                        bytesSent += await _socket
                            .SendAsync(new ArraySegment<byte>(AsyncTcp.HeaderBytes(type), bytesSent, 8 - bytesSent), SocketFlags.None)
                            .ConfigureAwait(false);
                    }
                }
                catch { }

                // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
                if (type == AsyncTcp.ErrorType)
                {
                    ShutDown();
                }

                return;
            }

            var bytes = await AsyncTcp.PeerHandler.GetMessageBytes(this, type, payload).ConfigureAwait(false);
            var totalSize = bytes.Length + 8;
            byte[] sendBytes = null;

            try
            {
                sendBytes = ArrayPool<byte>.Shared.Rent(totalSize);
                WriteInt(sendBytes, 0, type);
                WriteInt(sendBytes, 4, bytes.Length);
                Array.Copy(bytes, 0, sendBytes, 8, bytes.Length);

                int bytesSent = 0;
                while (bytesSent < totalSize)
                {
                    bytesSent += await _socket
                        .SendAsync(new ArraySegment<byte>(sendBytes, bytesSent, totalSize - bytesSent), SocketFlags.None)
                        .ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                ArrayPool<byte>.Shared.Return(sendBytes);
            }

            // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
            if (type == AsyncTcp.ErrorType)
            {
                ShutDown();
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

        private readonly int delayMS = 1000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task KeepAlive()
        {
            var count = AsyncTcp.KeepAliveInterval;

            await Task.Delay(delayMS).ConfigureAwait(false);

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

                await Task.Delay(delayMS).ConfigureAwait(false);
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