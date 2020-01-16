using System;
using System.Net;
using System.Threading.Tasks;

namespace ClientApp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var scenarios = new ClientScenarios(IPAddress.Loopback);
            //await scenarios.RunKeepAliveScenarioAsync(20).ConfigureAwait(false);
            await scenarios.RunDataScenarioAsync(100).ConfigureAwait(false);
            await Console.In.ReadLineAsync().ConfigureAwait(false);
        }
    }
}