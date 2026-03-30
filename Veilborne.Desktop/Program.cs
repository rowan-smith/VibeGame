using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veilborne.Core;
using Veilborne.Core.Biomes;
using Veilborne.Core.Biomes.Spawners;
using Veilborne.Core.Camera;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Logging;
using Veilborne.Core.Objects;
using Veilborne.Core.Stubs;
using Veilborne.Desktop.Ecs;
using Veilborne.Desktop.MonoGameImpl;

namespace Veilborne.Desktop;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        // Use original hosted service approach - MonoGame integration needs more work
        await RunWithHostedService(args);
    }

    private static async Task RunWithHostedService(string[] args)
    {
        using var logging = new LoggingService();
        var builder = Host.CreateApplicationBuilder(args);

        // Register all core services via extension
        builder.Services.AddVeilborneCoreServices();

        // Platform-specific and MonoGame-specific registrations
        builder.Services.AddSingleton<ProxyTerrainRenderer>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => sp.GetRequiredService<ProxyTerrainRenderer>());
        builder.Services.AddSingleton<IWorldObjectRenderer, StubWorldObjectRenderer>();
        builder.Services.AddSingleton<EcsManager>();
        builder.Services.AddSingleton<IEcsRuntime>(sp => sp.GetRequiredService<EcsManager>());
        builder.Services.AddSingleton<IInputProvider, MonoGameInputProvider>();
        builder.Services.AddSingleton<ITimeService, MonoGameTimeService>();
        builder.Services.AddSingleton<MonoGameGraphicsProvider>();
        builder.Services.AddSingleton<IGraphicsProvider>(sp => sp.GetRequiredService<MonoGameGraphicsProvider>());
        builder.Services.AddSingleton<IGameLoopHost>(sp => sp.GetRequiredService<MonoGameGraphicsProvider>());
        // UI provider will be initialized after MonoGame is ready

        // Load biomes dynamically (host-specific)
        RegisterBiomes(builder.Services);

        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();

        builder.Services.AddHostedService<Entry>();
        builder.Services.AddTransient<IGameEngine, VeilborneEngine>();

        var host = builder.Build();
        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }

    private static void RegisterBiomes(IServiceCollection services)
    {
        string baseDir = AppContext.BaseDirectory;
        string coreBiomes = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "biomes"));
        string desktopBiomes = Path.Combine(baseDir, "assets", "config", "biomes");
        var biomesDir = Directory.Exists(coreBiomes) ? coreBiomes : desktopBiomes;
        if (!Directory.Exists(biomesDir)) return;

        foreach (var file in Directory.GetFiles(biomesDir, "*.json"))
        {
            var dto = JsonModelLoader.LoadFile<BiomeData>(file);
            if (!dto.Enabled) continue;
            ValidateBiomeConfig(file, dto);

            services.AddSingleton<IBiome>(sp =>
            {
                var trees = sp.GetRequiredService<IWorldObjectRegistry>();
                var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                var envTerrain = sp.GetRequiredService<ITerrainGenerator>();
                var config = sp.GetRequiredService<IWorldConfigService>();
                return new ConfigBiome(dto.Id, dto, new ConfigTreeWorldObjectSpawner(trees, sampler, envTerrain, config, dto.AllowedObjects));
            });
        }
    }

    private static void ValidateBiomeConfig(string file, BiomeData biome)
    {
        if (string.IsNullOrWhiteSpace(biome.Id))
            throw new InvalidOperationException($"Biome config missing Id: {file}");
        if (biome.TerrainLayers == null)
            throw new InvalidOperationException($"Biome '{biome.Id}' missing TerrainLayers: {file}");
        if (string.IsNullOrWhiteSpace(biome.TerrainLayers.SurfaceTextureId) ||
            string.IsNullOrWhiteSpace(biome.TerrainLayers.SubsurfaceTextureId) ||
            string.IsNullOrWhiteSpace(biome.TerrainLayers.DeepTextureId))
            throw new InvalidOperationException($"Biome '{biome.Id}' has invalid TerrainLayers texture ids: {file}");
        if (biome.Mining?.Ores == null)
            throw new InvalidOperationException($"Biome '{biome.Id}' missing Mining.Ores: {file}");
        foreach (var ore in biome.Mining.Ores)
        {
            if (string.IsNullOrWhiteSpace(ore.OreType))
                throw new InvalidOperationException($"Biome '{biome.Id}' has an ore rule with empty OreType: {file}");
        }
    }
}
