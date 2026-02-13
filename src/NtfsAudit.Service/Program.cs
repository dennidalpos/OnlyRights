using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NtfsAudit.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    services.AddHostedService<ScanWorker>();
                })
                .Build()
                .Run();
        }
    }
}
