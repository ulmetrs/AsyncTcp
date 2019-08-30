using System.IO;
using System.Net.Sockets;

namespace AsyncTcp
{
   public class AsyncPeer
    {
        // Client socket.  
        public Socket socket;
        // Receive buffer
        public byte[] recvBuffer;
        // Receive state vars
        public int dataType = -1;
        public int dataSize = -1;
        public MemoryStream stream;
        // Send state vars
        public byte[] sendBuffer = null;
        public int sendIndex = -1;
        public AsyncPeer(int recvBufferSize)
        {
            recvBuffer = new byte[recvBufferSize];
            stream = new MemoryStream();
        }
    }
}