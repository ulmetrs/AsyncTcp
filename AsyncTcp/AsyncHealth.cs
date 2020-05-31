using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AsyncTcp
{
    // Health Check Server that just accepts connections and closes them
    // AWS Network LB Requires this behaviour to register targets
    public class AsyncHealth
    {
        public const string HostnameMessage = "\tHostname : {0}\tIP : {1}\tPort : {2}";

        private Socket _listener;
        private bool _alive;

        public string HostName { get; private set; }

        public async Task Start(IPAddress address = null, int bindPort = 9050)
        {
            if (_alive)
                throw new Exception("Cannot start, server is running");

            if (address == null)
                address = await Utils.GetIPAddress().ConfigureAwait(false);

            _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            _listener.Bind(new IPEndPoint(address, bindPort));
            _listener.Listen(100);

            _alive = true;

            HostName = address.ToString();

            Socket socket;
            try
            {
                while (true)
                {
                    socket = await _listener.AcceptAsync().ConfigureAwait(false);
                    try { socket.Shutdown(SocketShutdown.Both); } catch { }
                    try { socket.Close(); } catch { }
                }
            }
            catch { }

            ShutDown();
        }

        public void ShutDown()
        {
            _alive = false;
            try { _listener.Shutdown(SocketShutdown.Both); } catch { }
            try { _listener.Close(); } catch { }
        }
    }
}