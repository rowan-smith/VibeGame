using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Veilborne.Core;
using Veilborne.Core.Infrastructure;

namespace Veilborne.Windows;

class Program
{
    static void Main(string[] args)
    {
        using var logging = new LoggingService();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.AddGameServices();
        builder.Services.AddWindowsGameServices();

        builder.Services.AddHostedService<Entry>();

        var host = builder.Build();

        host.Run();
    }
}

