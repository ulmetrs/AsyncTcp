using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncPeer
    {
        private static long GlobalPeerId = -1;
        public long PeerId { get; } = Interlocked.Increment(ref GlobalPeerId);
        public IPEndPoint EndPoint { get; }
        public IPEndPoint UdpEndpoint { get; set; }

        private readonly Socket _socket;
        private readonly SemaphoreSlim _sendLock;
        private bool _alive;
        private int _keepAliveCount;

        // Peers are constructed from connected sockets
        internal AsyncPeer(Socket socket)
        {
            _socket = socket;
            _sendLock = new SemaphoreSlim(1, 1);
            EndPoint = (IPEndPoint)socket.RemoteEndPoint;
        }

        // The idea here is that every connected peer "Processes" until its done, the peer should not be reused.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal async Task Process()
        {
            _alive = true;

            var receiveTask = ProcessReceive();
            var keepAliveTask = KeepAlive();

            await AsyncTcp.PeerHandler.PeerConnected(this).ConfigureAwait(false);

            // Wait for sockets to close and parsing to finish
            await receiveTask.ConfigureAwait(false);

            _alive = false;

            await keepAliveTask.ConfigureAwait(false);

            await AsyncTcp.PeerHandler.PeerDisconnected(this).ConfigureAwait(false);
        }

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
            var info = new ParseInfo(); // 1 Alloc for reference values

            var reader = PipeReader.Create(new NetworkStream(_socket), AsyncTcp.ReceivePipeOptions);

            try
            {
                while (true)
                {
                    var result = await reader.ReadAsync().ConfigureAwait(false);
                    var buffer = result.Buffer;

                    // Handle Keep Alive Check
                    if (info.Type == AsyncTcp.KeepAliveType)
                    {
                        // Reset our KeepAliveCount to indicate we have received some data from the client
                        Interlocked.Exchange(ref _keepAliveCount, 0);
                    }

                    // Parse as many messages as we can from the buffer
                    while (TryParseBuffer(ref buffer, info))
                    {
                        await AsyncTcp.PeerHandler.Receive(this, info.Type, info.Buffer).ConfigureAwait(false);
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

            AsyncTcp.Log(PeerId, "COMPLETED RECEIVE");
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
                    buffer = buffer.Slice(8);
                    return true;
                }

                info.HeaderParsed = true;
            }

            if (buffer.Length < 8 + info.Size)
                return false;

            info.HeaderParsed = false; // Reset parse header flag
            info.Buffer = buffer.Slice(8, info.Size);
            buffer = buffer.Slice(8 + info.Size);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task Send(int type)
        {
            return Send(type, default(ReadOnlyMemory<byte>));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task Send(int type, ReadOnlyMemory<byte> buffer)
        {
            try
            {
                await _sendLock.WaitAsync().ConfigureAwait(false);

                int bytesSent;
                if (buffer.Length == 0)
                {
                    bytesSent = 0;
                    while (bytesSent < 8)
                    {
                        bytesSent += await _socket
                            .SendAsync(new ArraySegment<byte>(AsyncTcp.Headers(type), bytesSent, 8 - bytesSent), SocketFlags.None)
                            .ConfigureAwait(false);
                    }

                    // If the packet type is of ErrorType call Shutdown and let the peer gracefully finish processing
                    if (type == AsyncTcp.ErrorType)
                    {
                        ShutDown();
                    }

                    return;
                }

                var header = AsyncTcp.GetHeaderBuffer();
                WriteHeader(header, type, buffer.Length);

                bytesSent = 0;
                while (bytesSent < 8)
                {
                    bytesSent += await _socket
                        .SendAsync(new ArraySegment<byte>(header, bytesSent, 8 - bytesSent), SocketFlags.None)
                        .ConfigureAwait(false);
                }

                var segment = buffer.GetArray();

                bytesSent = 0;
                while (bytesSent < segment.Count)
                {
                    bytesSent += await _socket
                        .SendAsync(new ArraySegment<byte>(segment.Array, segment.Offset + bytesSent, segment.Count - bytesSent), SocketFlags.None)
                        .ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                _sendLock.Release();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteHeader(byte[] array, int type, int size)
        {
            MemoryMarshal.Write(new Span<byte>(array, 0, 4), ref type);
            MemoryMarshal.Write(new Span<byte>(array, 4, 4), ref size);
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
                        AsyncTcp.Log(PeerId, "KeepAlive Count >= 3, Shutting Down");
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