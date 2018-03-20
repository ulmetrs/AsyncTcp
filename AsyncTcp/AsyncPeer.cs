using System;
using System.IO;
using System.Net.Sockets;

namespace AsyncTcp
{
   public class AsyncPeer
    {
        // Client socket.  
        public Socket socket;
        // Receive buffer.
        public byte[] recvBuffer = new byte[1024];
        public int dataType = -1;
        public int dataSize = -1;
        public MemoryStream stream = new MemoryStream();
        public byte[] sendBuffer = null;
        public int sendIndex = -1;
    }
}