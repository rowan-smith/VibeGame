using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Veilborne.Core.Interfaces;
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
        using var logging = new LoggingService();

        var builder = Host.CreateApplicationBuilder(args);

        // Load enabled biome configs
        var biomesDir = Path.Combine(AppContext.BaseDirectory, "assets", "config", "biomes");
        if (!Directory.Exists(biomesDir))
            throw new InvalidOperationException($"Biomes directory not found: {biomesDir}");

        var biomeFiles = Directory.GetFiles(biomesDir, "*.json", SearchOption.TopDirectoryOnly);
        if (biomeFiles.Length == 0)
            throw new InvalidOperationException($"No biome configuration files (*.json) found in {biomesDir}");

        var enabledBiomes = new List<BiomeData>();
        foreach (var file in biomeFiles)
        {
            var dto = JsonModelLoader.LoadFile<BiomeData>(file);
            if (dto.Enabled)
                enabledBiomes.Add(dto);
        }

        if (enabledBiomes.Count == 0)
            throw new InvalidOperationException("No enabled biomes found. Enable at least one biome config file.");

        // Entry point
        builder.Services.AddHostedService<Entry>();

        // -----------------------------
        // Core terrain & environment
        // -----------------------------
        builder.Services.AddSingleton<ITerrainGenerator>(sp => new TerrainGenerator(new MultiNoiseConfig { Seed = WorldGlobals.Seed }));
        builder.Services.AddSingleton<ITerrainTextureRegistry, TerrainTextureRegistry>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => new TerrainRenderer(
            sp.GetRequiredService<ITextureManager>(),
            sp.GetRequiredService<ITerrainTextureRegistry>(),
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetServices<IBiome>()
        ));
        builder.Services.AddSingleton<ITreeRenderer, TreeRenderer>();
        builder.Services.AddSingleton<IWorldObjectRenderer, WorldObjectRenderer>();
        builder.Services.AddSingleton<ITreesRegistry, TreesRegistry>();
        builder.Services.AddSingleton<IEnvironmentSampler>(sp => new MultiNoiseSampler(new MultiNoiseConfig { Seed = WorldGlobals.Seed }));

        // -----------------------------
        // Biome registration
        // -----------------------------
        foreach (var def in enabledBiomes)
        {
            builder.Services.AddSingleton<IBiome>(sp =>
            {
                var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                var trees = sp.GetRequiredService<ITreesRegistry>();
                var envTerrain = sp.GetRequiredService<ITerrainGenerator>();
                IWorldObjectSpawner spawner = new ConfigTreeWorldObjectSpawner(trees, sampler, envTerrain, def.AllowedObjects);
                return new ConfigBiome(def.Id, def, spawner);
            });
        }

        builder.Services.AddSingleton<IBiomeProvider>(sp =>
        {
            var allBiomes = sp.GetServices<IBiome>().ToList();
            if (allBiomes.Count == 0)
            {
                throw new InvalidOperationException("No IBiome instances registered");
            }

            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var bp = new BiomeProviderConfig();
            var section = config.GetSection("Biomes:Provider");
            // Read configured values if present; fall back to defaults otherwise
            float avgSize = bp.AverageCellSize;
            float jitter = bp.Jitter;
            int seedVal = VibeGame.Core.WorldGlobals.Seed;

            var avgStr = section["AverageCellSize"]; if (!string.IsNullOrWhiteSpace(avgStr) && float.TryParse(avgStr, out var avgParsed)) avgSize = avgParsed;
            var jitStr = section["Jitter"]; if (!string.IsNullOrWhiteSpace(jitStr) && float.TryParse(jitStr, out var jitParsed)) jitter = jitParsed;
            var seedStr = section["Seed"]; if (!string.IsNullOrWhiteSpace(seedStr) && int.TryParse(seedStr, out var seedParsed)) seedVal = seedParsed;

            return new SimpleBiomeProvider(allBiomes, avgSize, seedVal, jitter);
        });

        // -----------------------------
        // Terrain services
        // -----------------------------
        builder.Services.AddSingleton<EditableTerrainService>();
        builder.Services.AddSingleton<ReadOnlyTerrainService>();
        builder.Services.AddSingleton<LowLodTerrainService>();

        var worldCfg = WorldGlobals.Config;
        builder.Services.AddSingleton(new TerrainRingConfig
        {
            EditableRadius = worldCfg?.EditableRadius ?? 3,
            ReadOnlyRadius = worldCfg?.ReadOnlyRadius ?? 6,
            LowLodRadius = worldCfg?.LowLodRadius ?? 12,
        });

        // TerrainManager orchestrates all rings
        builder.Services.AddSingleton<IInfiniteTerrain>(sp =>
        {
            var editable = sp.GetRequiredService<EditableTerrainService>();
            var readOnly = sp.GetRequiredService<ReadOnlyTerrainService>();
            var lowLod = sp.GetRequiredService<LowLodTerrainService>();
            var cfg = sp.GetRequiredService<TerrainRingConfig>();
            var biomeProvider = sp.GetRequiredService<IBiomeProvider>();
            var renderer = sp.GetRequiredService<ITerrainRenderer>();
            return new TerrainManager(editable, readOnly, cfg, biomeProvider, renderer, lowLod);
        });
        builder.Services.AddSingleton<TerrainManager>(sp => (TerrainManager)sp.GetRequiredService<IInfiniteTerrain>());

        // -----------------------------
        // Game engine & player
        // -----------------------------
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        builder.Services.AddSingleton<ITextureDownscaler, ImageSharpTextureDownscaler>();
        builder.Services.AddSingleton<ITextureManager, TextureManager>();
        builder.Services.AddSingleton<IItemRegistry, ItemRegistry>();

        builder.Services.AddSingleton(sp => new ObjectSpawner(
            WorldGlobals.Seed,
            sp.GetRequiredService<ITerrainGenerator>(),
            sp.GetRequiredService<IBiomeProvider>()));

        builder.Services.AddSingleton(sp => new Player(new Vector3(0f, 0f, 0f)));
        builder.Services.AddSingleton(sp => new World(
            WorldGlobals.Seed,
            sp.GetRequiredService<Player>(),
            sp.GetRequiredService<TerrainManager>(),
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetRequiredService<ObjectSpawner>()));

        // VibeGameEngine
        builder.Services.AddTransient<IGameEngine, VibeGameEngine>();

        var host = builder.Build();
        await host.StartAsync();
        await host.WaitForShutdownAsync();
    }
}
