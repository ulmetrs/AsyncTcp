using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static AsyncTcp.Utils;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    public class AsyncPeer
    {
        private static long GlobalPeerId;
        public long PeerId { get; }
        public Socket Socket;
        public MemoryStream Stream;
        public SemaphoreSlim SendLock { get; }
        public int DataType = -1;
        public int DataSize = -1;

        public AsyncPeer(Socket sock)
        {
            Socket = sock;
            Stream = new MemoryStream();
            SendLock = new SemaphoreSlim(1, 1);

            PeerId = Interlocked.Read(ref GlobalPeerId);
            Interlocked.Increment(ref GlobalPeerId);
        }

        public async Task SendAsync(int dataType, byte[] data)
        {
            var dataSize = data?.Length ?? 0;

            var bufferSize = ByteOffsetSize + dataSize;

            var buffer = ArrayPool<byte>.Shared.Rent(ByteOffsetSize + dataSize);

            WriteSendBytes(dataType, dataSize, data, buffer);

            await SendLock.WaitAsync().ConfigureAwait(false);

            await SendBufferAsync(bufferSize, buffer).ConfigureAwait(false);

            SendLock.Release();

            ArrayPool<byte>.Shared.Return(buffer);
        }

        // TODO convert to task when unity can support writeasync
        private void WriteSendBytes(int dataType, int dataSize, byte[] data, byte[] buffer)
        {
            using (MemoryStream stream = new MemoryStream(buffer))
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(dataType);
                    writer.Write(dataSize);
                    // Some message types have no payload
                    if (data != null)
                    {
                        writer.Write(data, 0, dataSize);
                    }
                }
            }
        }

        private async Task SendBufferAsync(int bufferSize, byte[] buffer)
        {
            var offset = 0;
            try
            {
                while (offset < bufferSize)
                {
                    offset += await Socket.SendAsync(new ArraySegment<byte>(buffer, offset, bufferSize - offset), 0).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            { await LogErrorAsync(e, SendErrorMessage, false).ConfigureAwait(false); }
        }

        public async Task SendKeepAliveAsync()
        {
            await SendLock.WaitAsync().ConfigureAwait(false);

            var offset = 0;
            try
            {
                while (offset < ByteOffsetSize)
                {
                    offset += await Socket.SendAsync(new ArraySegment<byte>(KABytes, offset, ByteOffsetSize - offset), 0).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            { await LogErrorAsync(e, SendErrorMessage, false).ConfigureAwait(false); }

            SendLock.Release();
        }

        public async Task ProcessBytes(byte[] buffer, int bytesRead, IAsyncHandler asyncHandler)
        {
            // Write the buffer bytes to the peer's message stream
            // TODO change to writeasync when unity supports it
            Stream.Write(buffer, 0, bytesRead);
            // Parse the stream
            await ParseReceive(asyncHandler).ConfigureAwait(false);
        }

        private async Task ParseReceive(IAsyncHandler asyncHandler)
        {
            BinaryReader reader;

            // Investigate various buffer sizes, having a reader and a writer, etc.
            // If I was fancy I could try larger recv buffers and use the BeginReceive index for subsequent calls, but not necessary currently

            // We have not yet read our message header (data size< 0) but have enough bytes to (stream position >= 8)
            if (DataSize < 0 && Stream.Position >= ByteOffsetSize)
            {
                // Store our write position to set back
                long writePos = Stream.Position;
                // Seek to the beginning of our data type
                Stream.Seek(0, SeekOrigin.Begin);
                // Read the data type and size ints
                reader = new BinaryReader(Stream); // We don't want to close the stream, so no 'using' statement
                DataType = reader.ReadInt32();
                DataSize = reader.ReadInt32();
                // Seek back to our current write position
                Stream.Seek(writePos, SeekOrigin.Begin);
            }

            // If we havn't yet read our data size, or our stream position is < data size we have more data to read
            if (DataSize < 0 || (Stream.Position < (DataSize + 8)))
            {
                return;
            }
            // We have read enough data to complete a message
            else
            {
                byte[] data = null;
                // If we actually have a payload (sometimes we have 0 data size)
                if (DataSize > 0)
                {
                    // Seek to the beginning of our data (byte 8)
                    Stream.Seek(ByteOffsetSize, SeekOrigin.Begin);
                    // Create a data-sized array for our callback
                    data = ArrayPool<byte>.Shared.Rent(DataSize);
                    // Read up to our data boundary
                    Stream.Read(data, 0, DataSize);
                }
                // Call the handler with our copied data, type, and size
                try
                {
                    await asyncHandler.DataReceived(this, DataType, data).ConfigureAwait(false);
                }
                catch (Exception e)
                { await LogErrorAsync(e, ParseReceiveErrorMessage, false).ConfigureAwait(false); }
                // Return our data array
                if (data != null)
                { ArrayPool<byte>.Shared.Return(data); }
                // Reset our state variables
                DataType = -1;
                DataSize = -1;
                // Create a new stream
                MemoryStream newStream = new MemoryStream();
                // Copy all remaining data to the new stream (tcp can string together message bytes)
                Stream.CopyTo(newStream);
                // Dispose our old stream
                Stream.Dispose();
                // Set the peer's stream to the new stream
                Stream = newStream;
                // Parse the new stream, our stream may have contained multiple messages
                await ParseReceive(asyncHandler).ConfigureAwait(false);
            }
        }
    }
}