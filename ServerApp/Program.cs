using System;
using System.Threading.Tasks;

namespace ServerApp
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var scenarios = new ServerScenarios();
            await scenarios.RunServer().ConfigureAwait(false);
            await Console.In.ReadLineAsync().ConfigureAwait(false);
        }
    }
}