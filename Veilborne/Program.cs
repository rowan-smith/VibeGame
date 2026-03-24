using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veilborne.Biomes;
using Veilborne.Biomes.Environment;
using Veilborne.Biomes.Spawners;
using Veilborne.Camera;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.Items;
using Veilborne.Core.TerrainTexture;
using Veilborne.Core.WorldObjects;
using Veilborne.Interfaces;
using Veilborne.Logging;
using Veilborne.Objects;
using Veilborne.Terrain;
using Veilborne.Core.RaylibImpl;

namespace Veilborne;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var logging = new LoggingService();
        var builder = Host.CreateApplicationBuilder(args);

        // Bootstrap Core configuration
        var configService = new WorldConfigService();
        builder.Services.AddSingleton<IWorldConfigService>(configService);

        // Core Input, Time, Graphics and UI
        builder.Services.AddSingleton<IInputProvider, RaylibInputProvider>();
        builder.Services.AddSingleton<ITimeService, RaylibTimeService>();
        builder.Services.AddSingleton<IGraphicsProvider, RaylibGraphicsProvider>();
        builder.Services.AddSingleton<IUiProvider, RaylibUiProvider>();

        // Core terrain & environment
        builder.Services.AddSingleton<ITerrainGenerator>(sp => new TerrainGenerator(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));
        builder.Services.AddSingleton<ITerrainTextureRegistry, TerrainTextureRegistry>();
        builder.Services.AddSingleton<TerrainTextureStreamingManager>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => new RaylibTerrainRenderer(
            sp.GetRequiredService<ITextureManager>(),
            sp.GetRequiredService<ITerrainTextureRegistry>(),
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetServices<IBiome>(),
            sp.GetRequiredService<TerrainTextureStreamingManager>()
        ));
        builder.Services.AddSingleton<ITreeRenderer, TreeRenderer>();
        builder.Services.AddSingleton<IWorldObjectRenderer, RaylibWorldObjectRenderer>();
        
        // ECS Systems
        builder.Services.AddSingleton<ISystem, PlayerSystem>();
        builder.Services.AddSingleton<ISystem, TerrainUpdateSystem>();
        builder.Services.AddSingleton<IRenderSystem, TerrainRenderSystem>();
        builder.Services.AddSingleton<IRenderSystem>(sp => (IRenderSystem)sp.GetRequiredService<IWorldObjectRenderer>());

        builder.Services.AddSingleton<ITreesRegistry, TreesRegistry>();
        builder.Services.AddSingleton<IEnvironmentSampler>(sp => new MultiNoiseSampler(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));

        // Load biomes dynamically
        RegisterBiomes(builder.Services);

        builder.Services.AddSingleton<IBiomeProvider>(sp =>
        {
            var config = sp.GetRequiredService<IWorldConfigService>();
            return new SimpleBiomeProvider(sp.GetServices<IBiome>(), config.BiomeProviderConfig.AverageCellSize, config.Seed, config.BiomeProviderConfig.Jitter);
        });

        // Terrain services
        builder.Services.AddSingleton<EditableTerrainService>();
        builder.Services.AddSingleton<ReadOnlyTerrainService>();
        builder.Services.AddSingleton<LowLodTerrainService>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IWorldConfigService>().TerrainConfig);

        builder.Services.AddSingleton<IInfiniteTerrain>(sp =>
        {
            var config = sp.GetRequiredService<IWorldConfigService>();
            return new TerrainManager(
                sp.GetRequiredService<EditableTerrainService>(),
                sp.GetRequiredService<ReadOnlyTerrainService>(),
                config.TerrainConfig,
                sp.GetRequiredService<IBiomeProvider>(),
                sp.GetRequiredService<ITerrainRenderer>(),
                config,
                sp.GetRequiredService<ITimeService>(),
                sp.GetRequiredService<LowLodTerrainService>());
        });
        builder.Services.AddSingleton<TerrainManager>(sp => (TerrainManager)sp.GetRequiredService<IInfiniteTerrain>());

        // Game engine & state
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        builder.Services.AddSingleton<ITextureManager, RaylibTextureManager>();
        builder.Services.AddSingleton<IItemRegistry, ItemRegistry>();

        builder.Services.AddSingleton(sp => new ObjectSpawner(
            sp.GetRequiredService<IWorldConfigService>().Seed,
            sp.GetRequiredService<ITerrainGenerator>(),
            sp.GetRequiredService<IBiomeProvider>()));

        // ECS
        builder.Services.AddSingleton<EntityRegistry>();
        builder.Services.AddSingleton(new Player(Vector3.Zero));
        builder.Services.AddSingleton(sp => new World(
            sp.GetRequiredService<IWorldConfigService>().Seed,
            sp.GetRequiredService<Player>(),
            sp.GetRequiredService<TerrainManager>(),
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetRequiredService<ObjectSpawner>(),
            sp.GetRequiredService<EntityRegistry>()));

        builder.Services.AddHostedService<Entry>();
        builder.Services.AddTransient<IGameEngine, VibeGameEngine>();

        var host = builder.Build();
        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }

    private static void RegisterBiomes(IServiceCollection services)
    {
        var biomesDir = Path.Combine(AppContext.BaseDirectory, "assets", "config", "biomes");
        if (!Directory.Exists(biomesDir)) return;

        foreach (var file in Directory.GetFiles(biomesDir, "*.json"))
        {
            var dto = JsonModelLoader.LoadFile<BiomeData>(file);
            if (!dto.Enabled) continue;

            services.AddSingleton<IBiome>(sp =>
            {
                var trees = sp.GetRequiredService<ITreesRegistry>();
                var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                var envTerrain = sp.GetRequiredService<ITerrainGenerator>();
                var config = sp.GetRequiredService<IWorldConfigService>();
                return new ConfigBiome(dto.Id, dto, new ConfigTreeWorldObjectSpawner(trees, sampler, envTerrain, config, dto.AllowedObjects));
            });
        }
    }
}
