using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Veilborne.Infrastructure;

namespace Veilborne;

class Program
{
    static void Main(string[] args)
    {
        using var logging = new LoggingService();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.RegisterGameServices();

        builder.Services.AddHostedService<Entry>();

        var host = builder.Build();

        host.Run();
    }
}
