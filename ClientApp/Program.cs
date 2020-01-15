using System;
using System.Threading.Tasks;

namespace ClientApp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var scenarios = new Scenarios("10.0.75.1");
            await scenarios.RunKeepAliveScenarioAsync(1).ConfigureAwait(false);
            await Console.In.ReadLineAsync().ConfigureAwait(false);
        }
    }
}
