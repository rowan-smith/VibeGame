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
        builder.Services.AddSingleton<ITerrainRenderer, TerrainRenderer>();
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
                IWorldObjectSpawner spawner = new ConfigTreeWorldObjectSpawner(trees, sampler, def.AllowedObjects);
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

            return new SimpleBiomeProvider(allBiomes);
        });

        // -----------------------------
        // Terrain services
        // -----------------------------
        builder.Services.AddSingleton<ReadOnlyTerrainService>(sp => new ReadOnlyTerrainService(
            sp.GetRequiredService<IBiomeProvider>(),
            sp.GetRequiredService<ITerrainRenderer>(),
            sp.GetRequiredService<ITerrainGenerator>()));
        builder.Services.AddSingleton<EditableTerrainService>();
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
            return new TerrainManager(editable, readOnly, cfg, biomeProvider, lowLod);
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
