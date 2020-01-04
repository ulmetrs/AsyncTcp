using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Values;

namespace AsyncTcp
{
    //public class AsyncBase
    //{
    //    protected IAsyncHandler _handler;

    //    // Right now we are doing double allocations on the server anyway when we serialize the object, then copy it into this
    //    // Stream. If we want to optimize sends with an ArrayPool then we need to optimize both sides
    //    // Send a message to the remote peer
    //    public async Task Send(AsyncPeer peer, int dataType, int dataSize, byte[] data)
    //    {
    //        //_logger.LogInformation("Begining Send, Type : " + dataType + " - Size : " + dataSize);
    //        // Calc total size of the send bytes
    //        var totalSize = dataSize + 8;
    //        // Rent a buffer
    //        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);
    //        //var buffer = new byte[totalSize];

    //        try
    //        {
    //            // Write our data into the buffer
    //            using (MemoryStream stream = new MemoryStream(buffer))
    //            {
    //                using (BinaryWriter writer = new BinaryWriter(stream))
    //                {
    //                    writer.Write(dataType);
    //                    writer.Write(dataSize);
    //                    // Some packets have no additional data
    //                    if (data != null)
    //                    {
    //                        writer.Write(data, 0, dataSize);
    //                    }
    //                }
    //            }

    //            // Lock our sendasync to ensure message bytes are contiguous
    //            await peer.SendLock.WaitAsync().ConfigureAwait(false);
    //            // Keep sending bytes until done
    //            var offset = 0;
    //            while (offset < totalSize)
    //            {
    //                offset += await peer.Socket.SendAsync(new ArraySegment<byte>(buffer, offset, totalSize - offset), 0).ConfigureAwait(false);
    //            }
    //        }
    //        catch (Exception e)
    //        {
    //            Console.WriteLine("Send Error: " + e.ToString());
    //        }
    //        finally
    //        {
    //            // Unlock to allow other sends
    //            peer.SendLock.Release();
    //            // Return the buffer
    //            ArrayPool<byte>.Shared.Return(buffer);
    //        }
    //    }

    //    protected async Task ParseReceive(AsyncPeer peer)
    //    {
    //        // Investigate various buffer sizes, having a reader and a writer, etc.
    //        // If I was fancy I could try larger recv buffers and use the BeginReceive index for subsequent calls, but not necessary currently

    //        // We have not yet read our message header (data size< 0) but have enough bytes to (stream position >= 8)
    //        if (peer.DataSize < 0 && peer.Stream.Position >= 8)
    //        {
    //            // Store our write position to set back
    //            long writePos = peer.Stream.Position;
    //            // Seek to the beginning of our data type
    //            peer.Stream.Seek(0, SeekOrigin.Begin);
    //            // Read the data type and size ints
    //            BinaryReader reader = new BinaryReader(peer.Stream); // We don't want to close the stream, so no 'using' statement
    //            peer.DataType = reader.ReadInt32();
    //            peer.DataSize = reader.ReadInt32();
    //            // Seek back to our current write position
    //            peer.Stream.Seek(writePos, SeekOrigin.Begin);
    //        }

    //        // If we havn't yet read our data size, or our stream position is < data size we have more data to read
    //        if (peer.DataSize < 0 || (peer.Stream.Position < (peer.DataSize + 8)))
    //        {
    //            return;
    //        }
    //        // We have read enough data to complete a message
    //        else
    //        {
    //            byte[] data = null;
    //            // If we actually have a payload (sometimes we have 0 data size)
    //            if (peer.DataSize > 0)
    //            {
    //                // Store our write position
    //                long pos = peer.Stream.Position;
    //                // Seek to the beginning of our data (byte 8)
    //                peer.Stream.Seek(8, SeekOrigin.Begin);
    //                // Create a data-sized array for our callback
    //                data = new byte[peer.DataSize]; // This is where im concerned with memory
    //                // Read up to our data boundary
    //                peer.Stream.Read(data, 0, peer.DataSize);
    //            }
    //            // Call the handler with our copied data, type, and size
    //            try
    //            {
    //                await _handler.DataReceived(peer, peer.DataType, peer.DataSize, data).ConfigureAwait(false);
    //            }
    //            catch (Exception e)
    //            {
    //                Console.WriteLine(string.Format(ParseReceiveErrorMessage, e.ToString()));
    //            }
    //            // Reset our state variables
    //            peer.DataType = -1;
    //            peer.DataSize = -1;
    //            // Create a new stream
    //            MemoryStream newStream = new MemoryStream();
    //            // Copy all remaining data to the new stream (tcp can string together message bytes)
    //            peer.Stream.CopyTo(newStream);
    //            // Dispose our old stream
    //            peer.Stream.Dispose();
    //            // Set the peer's stream to the new stream
    //            peer.Stream = newStream;
    //            // Parse the new stream, our stream may have contained multiple messages
    //            await ParseReceive(peer).ConfigureAwait(false);
    //        }
    //    }

    //    protected async Task SendKeepAlive(AsyncPeer peer)
    //    {
    //        try
    //        {
    //            // Lock our sendasync to ensure message bytes are contiguous
    //            await peer.SendLock.WaitAsync().ConfigureAwait(false);
    //            // Keep sending bytes until done
    //            var offset = 0;
    //            while (offset < 8)
    //            {
    //                offset += await peer.Socket.SendAsync(new ArraySegment<byte>(KABytes, offset, 8 - offset), 0).ConfigureAwait(false);
    //            }
    //        }
    //        catch (Exception e)
    //        {
    //            Console.WriteLine("SendKeepAlive Error: " + e.ToString());
    //        }
    //        finally
    //        {
    //            // Unlock to allow other sends
    //            peer.SendLock.Release();
    //        }
    //    }
    //}
}