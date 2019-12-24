using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    public class AsyncBase
    {
        protected AsyncHandler _handler;

        // Right now we are doing double allocations on the server anyway when we serialize the object, then copy it into this
        // Stream. If we want to optimize sends with an ArrayPool then we need to optimize both sides
        // Send a message to the remote peer
        public async Task Send(AsyncPeer peer, int dataType, int dataSize, byte[] data)
        {
            // Calc total size of the send bytes
            var totalSize = dataSize + 8;
            // Rent a buffer
            var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

            try
            {
                // Write our data into the buffer
                using (MemoryStream stream = new MemoryStream(buffer))
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(dataType);
                        writer.Write(dataSize);
                        // Some packets have no additional data
                        if (data != null)
                        {
                            writer.Write(data, 0, dataSize);
                        }
                    }
                }

                // Lock our sendasync to ensure message bytes are contiguous
                await peer.sendLock.WaitAsync();
                // Keep sending bytes until done
                var offset = 0;
                while (offset < totalSize)
                {
                    offset += await peer.socket.SendAsync(new ArraySegment<byte>(buffer, offset, totalSize - offset), 0);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Send Exception: " + e.ToString());
            }
            finally
            {
                // Unlock to allow other sends
                peer.sendLock.Release();
                // Return the buffer
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected async Task ParseReceive(AsyncPeer peer)
        {
            // Investigate various buffer sizes, having a reader and a writer, etc.
            // If I was fancy I could try larger recv buffers and use the BeginReceive index for subsequent calls, but not necessary currently

            // We have not yet read our message header (data size< 0) but have enough bytes to (stream position >= 8)
            if (peer.dataSize < 0 && peer.stream.Position >= 8)
            {
                // Store our write position to set back
                long writePos = peer.stream.Position;
                // Seek to the beginning of our data type
                peer.stream.Seek(0, SeekOrigin.Begin);
                // Read the data type and size ints
                BinaryReader reader = new BinaryReader(peer.stream); // We don't want to close the stream, so no 'using' statement
                peer.dataType = reader.ReadInt32();
                peer.dataSize = reader.ReadInt32();
                // Seek back to our current write position
                peer.stream.Seek(writePos, SeekOrigin.Begin);
            }

            // If we havn't yet read our data size, or our stream position is < data size we have more data to read
            if (peer.dataSize < 0 || (peer.stream.Position < (peer.dataSize + 8)))
            {
                return;
            }
            // We have read enough data to complete a message
            else
            {
                byte[] data = null;
                // If we actually have a payload (sometimes we have 0 data size)
                if (peer.dataSize > 0)
                {
                    // Store our write position
                    long pos = peer.stream.Position;
                    // Seek to the beginning of our data (byte 8)
                    peer.stream.Seek(8, SeekOrigin.Begin);
                    // Create a data-sized array for our callback
                    data = new byte[peer.dataSize]; // This is where im concerned with memory
                    // Read up to our data boundary
                    peer.stream.Read(data, 0, peer.dataSize);
                }
                // Call the handler with our copied data, type, and size
                try
                {
                    await _handler.DataReceived(peer, peer.dataType, peer.dataSize, data);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                }
                // Reset our state variables
                peer.dataType = -1;
                peer.dataSize = -1;
                // Create a new stream
                MemoryStream newStream = new MemoryStream();
                // Copy all remaining data to the new stream (tcp can string together message bytes)
                peer.stream.CopyTo(newStream);
                // Dispose our old stream
                peer.stream.Dispose();
                // Set the peer's stream to the new stream
                peer.stream = newStream;
                // Parse the new stream, our stream may have contained multiple messages
                await ParseReceive(peer);
            }
        }
    }
}