using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace AsyncTcp
{
   public class AsyncPeer
    {
        public Socket socket;
        public MemoryStream stream;
        public SemaphoreSlim sendLock;
        public int dataType = -1;
        public int dataSize = -1;

        public AsyncPeer(Socket sock, int recvBufferSize)
        {
            socket = sock;
            stream = new MemoryStream();
            sendLock = new SemaphoreSlim(1, 1);
        }
    }
}