namespace ServerApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var manager = new ServerManager();
            var serverTask = manager.Start();
            serverTask.Wait();
        }
    }
}