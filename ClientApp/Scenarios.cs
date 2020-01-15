using AsyncTest;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ClientApp
{
    public class Scenarios
    {
        private string _hostName;
        private XorShift _xorShifter;
        private Random _random;

        public Scenarios(string hostName)
        {
            _hostName = hostName;
            _xorShifter = new XorShift(true);
            _random = new Random();
        }

        public async Task RunKeepAliveScenarioAsync(int clientCount)
        {
            await Console.Out.WriteLineAsync($"Client creation count is {clientCount}.").ConfigureAwait(false);

            for (int i = 0; i < clientCount; i++)
            {
                var newClient = new ClientManager(_hostName);
                var task = Task.Run(newClient.Start);
            }

            await Console.Out.WriteLineAsync($"Successfully created Clients. Count: {clientCount}.").ConfigureAwait(false);
        }

        public async Task RunDataScenarioAsync()
        {
            var clients = new ClientManager[10];
            for (int i = 0; i < 10; i++)
            {
                clients[i] = new ClientManager(_hostName);
                var task = Task.Run(clients[i].Start);
            }

            await Task.Delay(10000).ConfigureAwait(false);

            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(DataTask(clients[i]));
                Console.WriteLine("Added task : " + i);
            }

            await Task.WhenAll(tasks);
        }

        private async Task DataTask(ClientManager client)
        {
            for (int i = 0; i < 1000; i++)
            {
                var letter = new TestMessage()
                {
                    index = i,
                    data = _xorShifter.GetRandomBytes(5000),
                };
                await client.AsyncClient.Send(1, letter).ConfigureAwait(false);
                await Task.Delay(_random.Next(1, 3) * 1000);
            }
        }
    }
}