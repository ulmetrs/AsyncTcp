using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    // Health Check Server that just accepts connections and closes them
    public class AsyncHealth
    {
        // Listening port
        private int _port;
        // Server kill bool
        private bool _stopServer;

        public AsyncHealth()
        {
            _stopServer = false;
        }

        public Task Start(int port)
        {
            _stopServer = false;
            _port = port;

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
                // Establish the local endpoint for the socket.  
                // The DNS name of the computer 
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                IPAddress ipAddress = ipHostInfo.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddress, _port);

                Console.WriteLine("hostname : " + Dns.GetHostName() + "   ip : " + ipAddress + "   port : " + _port);

                // Create a TCP/IP socket.  
                Socket listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
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