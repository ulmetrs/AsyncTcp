using System;
using System.Threading.Tasks;

namespace AsyncTester
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var keepAliveScenario = new KeepAliveScenario();
            await keepAliveScenario.RunScenarioAsync(1, 1).ConfigureAwait(false);

            await Console.In.ReadLineAsync().ConfigureAwait(false);

            var xorShifter = new XorShift(true);

            var bytes = xorShifter.GetRandomBytes(1000);
        }
    }
}
