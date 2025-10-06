using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeGame.Camera;
using VibeGame.Objects;
using VibeGame.Terrain;
using VibeGame.Core;

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
        builder.Services.AddSingleton<ITerrainGenerator, TerrainGenerator>();
        builder.Services.AddSingleton<ITerrainRenderer, TerrainRenderer>();
        builder.Services.AddSingleton<ITreeRenderer, TreeRenderer>();

        // Game engine services
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        builder.Services.AddSingleton<IInfiniteTerrain, ChunkedTerrainService>();
        builder.Services.AddSingleton<ITextureManager, TextureManager>();
        builder.Services.AddTransient<IGameEngine, VibeGameEngine>();

        var host = builder.Build();

        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }
}
