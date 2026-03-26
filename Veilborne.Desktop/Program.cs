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
using Veilborne.Core.Stubs;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.Items;
using Veilborne.Core.Settings;
using Veilborne.Core.TerrainTexture;
using Veilborne.Core.WorldObjects;
using Veilborne.Core.Sky;
using Veilborne.Interfaces;
using Veilborne.Logging;
using Veilborne.Objects;
using Veilborne.Terrain;
using Veilborne.Core.MonoGameImpl;

namespace Veilborne;

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

        // Bootstrap Core configuration
        var configService = new WorldConfigService();
        builder.Services.AddSingleton<IWorldConfigService>(configService);

        // Core Input, Time, Graphics and UI
        // Core providers (no MonoGame dependencies)
        builder.Services.AddSingleton<IInputProvider, MonoGameInputProvider>();
        builder.Services.AddSingleton<ITimeService, MonoGameTimeService>();
        builder.Services.AddSingleton<MonoGameGraphicsProvider>();
        builder.Services.AddSingleton<IGraphicsProvider>(sp => sp.GetRequiredService<MonoGameGraphicsProvider>());
        builder.Services.AddSingleton<IGameLoopHost>(sp => sp.GetRequiredService<MonoGameGraphicsProvider>());
        // UI provider will be initialized after MonoGame is ready

        // Core terrain & environment — proxy renderer: starts as stub, swapped to real MonoGame renderer after graphics init
        builder.Services.AddSingleton<ProxyTerrainRenderer>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => sp.GetRequiredService<ProxyTerrainRenderer>());
        builder.Services.AddSingleton<IWorldObjectRenderer, StubWorldObjectRenderer>(); // Stub for DI, real one from ECS manager
        builder.Services.AddSingleton<ITerrainGenerator>(sp => new TerrainGenerator(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));
        builder.Services.AddSingleton<ITerrainTextureRegistry, TerrainTextureRegistry>();
        builder.Services.AddSingleton<ISkyLightingService, SkyLightingService>();
        // ECS Systems (will be initialized manually after MonoGame is ready)
        builder.Services.AddSingleton<CleanupSystem>();
        builder.Services.AddSingleton<DependencySystem>();
        builder.Services.AddSingleton<InputSystem>();
        builder.Services.AddSingleton<DigInputSystem>();
        builder.Services.AddSingleton<DigProbeSystem>();
        builder.Services.AddSingleton<VoxelRaycastSystem>();
        builder.Services.AddSingleton<DepleteSystem>();
        builder.Services.AddSingleton<PatchRegenSystem>();
        builder.Services.AddSingleton<DigExecutionSystem>();
        builder.Services.AddSingleton<CameraSystem>();
        builder.Services.AddSingleton<PlayerSystem>(); // still used as implementation detail
        builder.Services.AddSingleton<PlayerInputSystem>();
        builder.Services.AddSingleton<AISystem>();
        builder.Services.AddSingleton<AnimationSystem>();
        builder.Services.AddSingleton<ParticleSystem>();
        builder.Services.AddSingleton<BiomeAssetTracker>();
        builder.Services.AddSingleton<BiomeDiscoverySystem>();
        builder.Services.AddSingleton<AssetLoadSystem>();
        builder.Services.AddSingleton<BiomePrepSystem>();
        builder.Services.AddSingleton<AssetUnloadSystem>();
        builder.Services.AddSingleton<CollisionFrameBuffer>();
        builder.Services.AddSingleton<RenderFrameState>();
        builder.Services.AddSingleton<CollisionDetectionSystem>();
        builder.Services.AddSingleton<CollisionResolutionSystem>();
        builder.Services.AddSingleton<ConstraintSystem>();
        builder.Services.AddSingleton<ForceSystem>();
        builder.Services.AddSingleton<IntegrationSystem>();
        builder.Services.AddSingleton<TerrainLoadSystem>();
        builder.Services.AddSingleton<TerrainLoadQueueSystem>();
        builder.Services.AddSingleton<TerrainLoadRequestTracker>();
        builder.Services.AddSingleton<TerrainGenSystem>();
        builder.Services.AddSingleton<VegetationSystem>();
        builder.Services.AddSingleton<ShadowMapSystem>();
        builder.Services.AddSingleton<EffectSystem>();
        builder.Services.AddSingleton<FrustumCullSystem>();
        builder.Services.AddSingleton<SortSystem>();
        builder.Services.AddSingleton<UISystem>();
        builder.Services.AddSingleton<DebugDrawSystem>();
        builder.Services.AddSingleton<CompositeRenderSystem>();
        builder.Services.AddSingleton<EcsManager>();
        builder.Services.AddSingleton<IEcsRuntime>(sp => sp.GetRequiredService<EcsManager>());

        builder.Services.AddSingleton<IWorldObjectRegistry, WorldObjectRegistry>();
        builder.Services.AddSingleton<IEnvironmentSampler>(sp => new MultiNoiseSampler(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));

        // Load biomes dynamically
        RegisterBiomes(builder.Services);

        builder.Services.AddSingleton<IBiomeProvider>(sp =>
        {
            var config = sp.GetRequiredService<IWorldConfigService>();
            var bcfg = config.BiomeProviderConfig;
            return new SimpleBiomeProvider(
                sp.GetServices<IBiome>(),
                bcfg.AverageCellSize,
                config.Seed,
                bcfg.Jitter,
                bcfg.WarpFrequencyScale,
                bcfg.WarpAmplitudeScale,
                bcfg.BlendWidthWorld);
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
                sp.GetRequiredService<IGameSettingsService>(),
                sp.GetRequiredService<LowLodTerrainService>());
        });
        builder.Services.AddSingleton<TerrainManager>(sp => (TerrainManager)sp.GetRequiredService<IInfiniteTerrain>());

        // Game engine & state
        builder.Services.AddSingleton<IGameSettingsService, GameSettingsService>();
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
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
}
