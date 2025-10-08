using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VibeGame.Biomes;
using VibeGame.Biomes.Environment;
using VibeGame.Biomes.Spawners;
using VibeGame.Camera;
using VibeGame.Core;
using VibeGame.Core.Downscalers;
using VibeGame.Core.Items;
using VibeGame.Core.WorldObjects;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var logging = new VibeLogging();

        var builder = Host.CreateApplicationBuilder(args);

        // Load biome configurations from individual files under assets\\config\\biomes
        var biomesDir = Path.Combine(AppContext.BaseDirectory, "assets", "config", "biomes");
        if (!Directory.Exists(biomesDir))
            throw new InvalidOperationException($"Biomes directory not found: {biomesDir}");

        var biomeFiles = Directory.GetFiles(biomesDir, "*.json", SearchOption.TopDirectoryOnly);
        if (biomeFiles.Length == 0)
            throw new InvalidOperationException($"No biome configuration files (*.json) found in {biomesDir}");

        var enabledBiomeDtos = new List<UnifiedBiomeDto>();
        foreach (var file in biomeFiles)
        {
            var dto = JsonModelLoader.LoadFile<UnifiedBiomeDto>(file);
            if (dto.Enabled)
                enabledBiomeDtos.Add(dto);
        }

        if (enabledBiomeDtos.Count == 0)
            throw new InvalidOperationException("No enabled biomes were found. Enable at least one biome config file.");

        // Add hosted service that will start your application
        builder.Services.AddHostedService<Entry>();

        // various services used in Entry.cs
        builder.Services.AddSingleton<ITerrainGenerator>(sp => new TerrainGenerator(new VibeGame.Biomes.Environment.MultiNoiseConfig { Seed = VibeGame.Core.WorldGlobals.Seed }));
        builder.Services.AddSingleton<ITerrainTextureRegistry, TerrainTextureRegistry>();
        builder.Services.AddSingleton<ITerrainRenderer, TerrainRenderer>();
        builder.Services.AddSingleton<ITreeRenderer, TreeRenderer>();
        builder.Services.AddSingleton<IWorldObjectRenderer, WorldObjectRenderer>();
        builder.Services.AddSingleton<ITreesRegistry, TreesRegistry>();
        builder.Services.AddSingleton<IEnvironmentSampler>(sp => new VibeGame.Biomes.Environment.MultiNoiseSampler(new VibeGame.Biomes.Environment.MultiNoiseConfig { Seed = VibeGame.Core.WorldGlobals.Seed }));

        // Biomes and providers
        // Register biomes from unified configuration (no hardcoded classes)
        foreach (var def in enabledBiomeDtos)
        {
            var captured = def; // avoid modified closure
            builder.Services.AddSingleton<IBiome>(sp =>
            {
                var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                var trees = sp.GetRequiredService<ITreesRegistry>();

                // Use config-driven world object spawner (trees) honoring AllowedObjects when provided
                IWorldObjectSpawner spawner = new ConfigTreeWorldObjectSpawner(
                    trees,
                    sampler,
                    captured.AllowedObjects
                );

                var data = captured.ToBiomeData();
                return new ConfigBiome(captured.Id, data, spawner);
            });
        }

        // Multi-noise environment sampling and biome provider
        // Sampler will use its internal defaults; configuration now driven by biomes.json profiles
        // Removed appsettings-based overrides and seed wiring as they are no longer required
        builder.Services.AddSingleton<IBiomeProvider>(sp =>
        {
            var sampler = sp.GetRequiredService<IEnvironmentSampler>();
            var allBiomes = sp.GetServices<IBiome>().ToList();

            // Create profiles from the unified enabled DTOs captured above
            var profiles = enabledBiomeDtos.Select(d => d.ToProfile()).ToList();

            // Validate that all enabled DTO ids are registered as biomes
            var dtoIds = new HashSet<string>(enabledBiomeDtos.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
            var registered = new HashSet<string>(allBiomes.Select(b => b.Id), StringComparer.OrdinalIgnoreCase);
            var missing = dtoIds.Except(registered, StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Enabled biomes not registered: {string.Join(", ", missing)}");

            return new MultiNoiseBiomeProvider(allBiomes, sampler, profiles);
        });

        // Game engine services
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        // Register base heightmap terrain as a concrete service
        builder.Services.AddSingleton<ReadOnlyTerrainService>();
        // Hybrid service composes the heightmap and adds local editable voxels
        builder.Services.AddSingleton<EditableTerrainService>();
        // Far distance low-LOD ring (coarse height mesh)
        builder.Services.AddSingleton<LowLodTerrainService>();
        
        // Ring configuration (optionally loaded from assets\\config\\world.json)
        var worldCfg = VibeGame.Core.WorldGlobals.Config;
        builder.Services.AddSingleton(new TerrainRingConfig
        {
            EditableRadius = worldCfg?.EditableRadius ?? 3,
            ReadOnlyRadius = worldCfg?.ReadOnlyRadius ?? 6,
            LowLodRadius = worldCfg?.LowLodRadius ?? 12,
            MaxActiveVoxelChunks = worldCfg?.MaxActiveVoxelChunks ?? 128
        });
        
        // Terrain manager orchestrates ring services and is used by the engine
        builder.Services.AddSingleton<IInfiniteTerrain, TerrainManager>();
        // Expose the same TerrainManager instance via its concrete type
        builder.Services.AddSingleton<TerrainManager>(sp => (TerrainManager)sp.GetRequiredService<IInfiniteTerrain>());

        // Register higher-level world systems
        builder.Services.AddSingleton(sp => new BiomeManager(
            VibeGame.Core.WorldGlobals.Seed,
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetRequiredService<ITerrainGenerator>()));
        builder.Services.AddSingleton(sp => new VibeGame.Objects.ObjectSpawner(
            VibeGame.Core.WorldGlobals.Seed,
            sp.GetRequiredService<ITerrainGenerator>(),
            sp.GetRequiredService<IBiomeProvider>()));
        builder.Services.AddSingleton(sp => new VibeGame.Core.Player(new System.Numerics.Vector3(0f, 0f, 0f)));
        builder.Services.AddSingleton(sp => new VibeGame.Core.World(
            VibeGame.Core.WorldGlobals.Seed,
            sp.GetRequiredService<VibeGame.Core.Player>(),
            sp.GetRequiredService<TerrainManager>(),
            sp.GetRequiredService<BiomeManager>(),
            sp.GetRequiredService<VibeGame.Objects.ObjectSpawner>()));

        // Register texture downscaler implementation
        builder.Services.AddSingleton<ITextureDownscaler, ImageSharpTextureDownscaler>();

        builder.Services.AddSingleton<ITextureManager, TextureManager>();
        builder.Services.AddSingleton<IItemRegistry, ItemRegistry>();
        builder.Services.AddTransient<IGameEngine, VibeGameEngine>();

        var host = builder.Build();

        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }
}
