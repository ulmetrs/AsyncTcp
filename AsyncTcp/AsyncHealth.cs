using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    // Health Check Server that just accepts connections and closes them
    public class AsyncHealth
    {
        private IPAddress _ipAddress;
        private int _bindPort;
        private bool _stopServer;

        public AsyncHealth()
        {
            _stopServer = false;
        }

        public Task Start(IPAddress ipAddress = null, int bindPort = 9050)
        {
            _ipAddress = ipAddress;
            if (_ipAddress == null)
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                _ipAddress = ipHostInfo.AddressList[0];
            }
            _bindPort = bindPort;

            _stopServer = false;

            // Start our server thread
            return Task.Run(() => Accept());
        }

        public void Stop()
        {
            _stopServer = true;
        }

        private void Accept()
        {
            try
            {
                Console.WriteLine("hostname : " + Dns.GetHostName() + "   ip : " + _ipAddress + "   port : " + _bindPort);

                // Establish the local endpoint for the socket.  
                IPEndPoint localEndPoint = new IPEndPoint(_ipAddress, _bindPort);
                // Create a TCP/IP socket.  
                Socket listener = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                Socket socket;

                // Bind the socket to the local endpoint and listen for incoming connections.
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    if (_stopServer)
                    {
                        return;
                    }
                    
                    socket = listener.Accept();
                    //Console.WriteLine("Accepted Health Socket, Open Handles : " + Process.GetCurrentProcess().HandleCount);
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                    socket = null;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}