using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace AsyncTcp
{
   public class AsyncPeer
    {
        public Socket Socket;
        public MemoryStream Stream;
        public SemaphoreSlim SendLock;
        public int DataType = -1;
        public int DataSize = -1;
        public bool Active = true;

        public AsyncPeer(Socket sock, int recvBufferSize)
        {
            Socket = sock;
            Stream = new MemoryStream();
            SendLock = new SemaphoreSlim(1, 1);
        }
    }
}