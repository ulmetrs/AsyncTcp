﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncTcp
{
    // Health Check Server that just accepts connections and closes them
    public class AsyncHealth
    {
        private IPAddress _ipAddress;
        private int _bindPort;

        private bool _serverRunning = false;

        public AsyncHealth()
        {
            // Nothing
        }

        public async Task Start(IPAddress ipAddress = null, int bindPort = 9050)
        {
            _ipAddress = ipAddress;
            if (_ipAddress == null)
            {
                IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                _ipAddress = ipHostInfo.AddressList[0];
            }
            _bindPort = bindPort;
            Console.WriteLine("hostname : " + Dns.GetHostName() + "   ip : " + _ipAddress + "   port : " + _bindPort);
            // Establish the local endpoint for the socket.  
            IPEndPoint localEndPoint = new IPEndPoint(_ipAddress, _bindPort);
            // Create a TCP/IP socket.  
            Socket listener = new Socket(_ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            // Bind the socket to the local endpoint and listen for incoming connections.
            listener.Bind(localEndPoint);
            listener.Listen(100);
            Socket socket;
            // Set server running
            _serverRunning = true;
            while (_serverRunning)
            {
                Thread.Sleep(1000);
                socket = await listener.AcceptAsync();
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
                socket = null;
            }
        }

        public void Stop()
        {
            _serverRunning = false;
        }
    }
}