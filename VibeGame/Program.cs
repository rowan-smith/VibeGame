using System.Net.Mime;
using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VibeGame;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var logging = new VibeLogging();

        var builder = Host.CreateApplicationBuilder(args);

        // Add hosted service that will start your application
        builder.Services.AddHostedService<Entry>();

        // various services used in Entry.cs
        builder.Services.AddTransient<IGameEngine, ThreeDEngine>();

        var host = builder.Build();

        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }
}
