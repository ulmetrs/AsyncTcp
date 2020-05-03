using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using static AsyncTcp.Logging;

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
                throw new Exception("Cannot Start, Server is running");

            if (address == null)
            {
                address = await Utils.GetIPAddress().ConfigureAwait(false);
            }

            _listener = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listener.NoDelay = true;
            _listener.Bind(new IPEndPoint(address, bindPort));
            _listener.Listen(100);

            HostName = address.ToString();

            await LogMessageAsync(string.Format(HostnameMessage, HostName, address, bindPort), false).ConfigureAwait(false);

            _alive = true;

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
            catch (Exception e)
            {
                await LogErrorAsync(e, "Accepted Loop Exception", true).ConfigureAwait(false);
            }

            ShutDown();
        }

        public void ShutDown()
        {
            _alive = false;

            // If we never connect listener.Shutdown throws an error, so try separately
            try { _listener.Shutdown(SocketShutdown.Both); } catch { }
            try { _listener.Close(); } catch { }
        }
    }
}