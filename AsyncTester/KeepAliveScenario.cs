using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AsyncTester
{
    public class KeepAliveScenario
    {
        private static readonly List<ServerManager> ServerManagers = new List<ServerManager>();
        private static readonly List<ClientManager> ClientManagers = new List<ClientManager>();

        public async Task RunScenarioAsync(int serverCount, int clientCount)
        {
            var hostName = await CreateServerManagersAsync(serverCount).ConfigureAwait(false);

            await CreateClientManagersAsync(hostName, clientCount).ConfigureAwait(false);
        }

        public async Task<string> CreateServerManagersAsync(int serverCount)
        {
            var hostName = string.Empty;

            await Console.Out.WriteLineAsync($"Server creation count is {serverCount}.").ConfigureAwait(false);

            for (int i = 0; i < serverCount; i++)
            {
                var newServer = new ServerManager();
                newServer.Start();
                await Task.Delay(2000).ConfigureAwait(false);

                if (i == 0)
                {
                    hostName = newServer.AsyncServer.ServerHostName;
                }

                await Console.Out.WriteLineAsync($"Created server {i}").ConfigureAwait(false);
                ServerManagers.Add(newServer);
            }

            await Console.Out.WriteLineAsync($"Successfully created Servers. Count: {serverCount}.").ConfigureAwait(false);
            return hostName;
        }

        public async Task CreateClientManagersAsync(string serverHostName, int clientCount)
        {
            await Console.Out.WriteLineAsync($"Client creation count is {clientCount}.").ConfigureAwait(false);

            for (int i = 0; i < clientCount; i++)
            {
                var newClient = new ClientManager(serverHostName);
                newClient.Start();

                await Console.Out.WriteLineAsync($"Created client {i}").ConfigureAwait(false);
                ClientManagers.Add(newClient);
            }

            await Console.Out.WriteLineAsync($"Successfully created Clients. Count: {clientCount}.").ConfigureAwait(false);
        }
    }
}
