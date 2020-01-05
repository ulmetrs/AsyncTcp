using System;
using System.Threading.Tasks;

namespace ClientApp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var scenarios = new Scenarios("192.168.99.1");
            await scenarios.RunDataScenarioAsync().ConfigureAwait(false);

            await Console.In.ReadLineAsync().ConfigureAwait(false);
        }
    }
}
