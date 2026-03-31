using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Veilborne.Core.Biomes;
using Veilborne.Core.Biomes.Environment;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Items;
using Veilborne.Core.Objects;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Core.Terrain;
using Veilborne.Core.TerrainTexture;
using Veilborne.Core.UI;
using Veilborne.Core.WorldObjects;

namespace Veilborne.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVeilborneCoreServices(this IServiceCollection services)
    {
        // Bootstrap Core configuration - platform must provide IWorldConfigService
        // services.AddSingleton<IWorldConfigService, WorldConfigService>();

        // Core Input, Time, Graphics and UI (abstract, not platform-specific)
        // Platform-specific implementations should be registered in the host project

        // Core terrain & environment (host must provide ITerrainRenderer and IWorldObjectRenderer implementations)
        services.AddSingleton<ITerrainGenerator>(sp => new TerrainGenerator(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));
        services.AddSingleton<ITerrainTextureRegistry, TerrainTextureRegistry>();
        services.AddSingleton<ISkyLightingService, SkyLightingService>();
        services.AddSingleton<IShadowMapService, CpuShadowMapService>();
        // ECS Systems
        services.AddSingleton<CleanupSystem>();
        services.AddSingleton<DependencySystem>();
        services.AddSingleton<InputSystem>();
        services.AddSingleton<DigInputSystem>();
        services.AddSingleton<DigProbeSystem>();
        services.AddSingleton<VoxelRaycastSystem>();
        services.AddSingleton<DepleteSystem>();
        services.AddSingleton<PatchRegenSystem>();
        services.AddSingleton<DigExecutionSystem>();
        services.AddSingleton<DigParticleSystem>();
        services.AddSingleton<CameraSystem>();
        services.AddSingleton<HotbarSelectionSystem>();
        services.AddSingleton<PlayerSystem>();
        services.AddSingleton<PlayerInputSystem>();
        services.AddSingleton<AISystem>();
        services.AddSingleton<AnimationSystem>();
        services.AddSingleton<ParticleSystem>();
        services.AddSingleton<BiomeAssetTracker>();
        services.AddSingleton<BiomeDiscoverySystem>();
        services.AddSingleton<AssetLoadSystem>();
        services.AddSingleton<BiomePrepSystem>();
        services.AddSingleton<AssetUnloadSystem>();
        services.AddSingleton<WorldObjectSpatialIndex>();
        services.AddSingleton<WorldObjectSpatialIndexSystem>();
        services.AddSingleton<CollisionFrameBuffer>();
        services.AddSingleton<EcsPerformanceMonitor>();
        services.AddSingleton<CollisionDetectionSystem>();
        services.AddSingleton<CollisionResolutionSystem>();
        services.AddSingleton<ConstraintSystem>();
        services.AddSingleton<ForceSystem>();
        services.AddSingleton<IntegrationSystem>();
        services.AddSingleton<TerrainLoadSystem>();
        services.AddSingleton<TerrainLoadQueueSystem>();
        services.AddSingleton<TerrainLoadRequestTracker>();
        services.AddSingleton<TerrainGenSystem>();
        services.AddSingleton<VegetationSystem>();
        services.AddSingleton<ShadowMapSystem>();
        services.AddSingleton<EffectSystem>();
        services.AddSingleton<UISystem>();
        services.AddSingleton<DebugDrawSystem>();
        services.AddSingleton<CompositeRenderSystem>();
        // ECS runtime (host must provide IEcsRuntime implementation)

        // IWorldObjectRegistry and IItemRegistry must be provided by host
        // services.AddSingleton<IWorldObjectRegistry, WorldObjectRegistry>();
        services.AddSingleton<IEnvironmentSampler>(sp => new MultiNoiseSampler(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));

        // Biome registration is host-specific (file system access)
        // BiomeProvider registration
        services.AddSingleton<IBiomeProvider>(sp =>
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
        services.AddSingleton<EditableTerrainService>();
        services.AddSingleton<ReadOnlyTerrainService>();
        services.AddSingleton<LowLodTerrainService>();
        services.AddSingleton(sp => sp.GetRequiredService<IWorldConfigService>().TerrainConfig);

        services.AddSingleton<IInfiniteTerrain>(sp =>
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
        services.AddSingleton<TerrainManager>(sp => (TerrainManager)sp.GetRequiredService<IInfiniteTerrain>());

        // Game engine & state
        services.AddSingleton<IGameSettingsService, GameSettingsService>();
        // services.AddSingleton<IItemRegistry, ItemRegistry>();
        services.AddSingleton<EntityRegistry>();
        services.AddSingleton(new Player(Vector3.Zero));
        services.AddSingleton(sp => new World(
            sp.GetRequiredService<IWorldConfigService>().Seed,
            sp.GetRequiredService<Player>(),
            sp.GetRequiredService<TerrainManager>(),
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetRequiredService<ObjectSpawner>(),
            sp.GetRequiredService<EntityRegistry>()));

        // UI controllers
        services.AddSingleton<HudUiController>();
        services.AddSingleton<DebugOverlayUiController>();

        // Object spawner
        services.AddSingleton(sp => new ObjectSpawner(
            sp.GetRequiredService<IWorldConfigService>().Seed,
            sp.GetRequiredService<ITerrainGenerator>(),
            sp.GetRequiredService<IBiomeProvider>()));

        return services;
    }
}
