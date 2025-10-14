using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Veilborne.Core;

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

        var host = builder.Build();

        // Resolve the game from DI
        var game = host.Services.GetRequiredService<VeilborneGame>();

        // Run the host asynchronously in background
        host.RunAsync();

        // Run MonoGame on the **main thread**
        game.Run();

        // Once the game exits, shutdown the host
        host.StopAsync().GetAwaiter().GetResult();
    }
}

