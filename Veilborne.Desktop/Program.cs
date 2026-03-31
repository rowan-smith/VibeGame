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
using Veilborne.Core.Items;
using Veilborne.Core.WorldObjects;
using Veilborne.Core.TerrainTexture;
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

        // Load and register registries (host-specific file loading)
        RegisterRegistries(builder.Services);

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

    private static void RegisterRegistries(IServiceCollection services)
    {
        string baseDir = AppContext.BaseDirectory;
        
        // 1. World Config
        string worldJsonPath = Path.Combine(baseDir, "assets", "config", "world.json");
        if (!File.Exists(worldJsonPath))
        {
            // Try dev path
            worldJsonPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "world.json"));
        }
        
        if (File.Exists(worldJsonPath))
        {
            var config = JsonModelLoader.LoadFile<WorldConfig>(worldJsonPath);
            services.AddSingleton<IWorldConfigService>(new WorldConfigService(config));
        }
        else
        {
            throw new FileNotFoundException("world.json not found", worldJsonPath);
        }

        // 2. Items
        string itemsDir = Path.Combine(baseDir, "assets", "config", "items");
        if (!Directory.Exists(itemsDir))
        {
            itemsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "items"));
        }

        var itemSets = new List<ItemConfigSet>();
        if (Directory.Exists(itemsDir))
        {
            foreach (var file in Directory.GetFiles(itemsDir, "*.json"))
            {
                itemSets.Add(JsonModelLoader.LoadFile<ItemConfigSet>(file));
            }
        }
        services.AddSingleton<IItemRegistry>(new ItemRegistry(itemSets));

        // 3. World Objects
        string woDir = Path.Combine(baseDir, "assets", "config", "world_objects");
        if (!Directory.Exists(woDir))
        {
            woDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "world_objects"));
        }

        var woConfigs = new List<WorldObjectsConfig>();
        if (Directory.Exists(woDir))
        {
            foreach (var file in Directory.GetFiles(woDir, "*.json"))
            {
                woConfigs.Add(JsonModelLoader.LoadFile<WorldObjectsConfig>(file));
            }
        }
        services.AddSingleton<IWorldObjectRegistry>(new WorldObjectRegistry(woConfigs));

        // 4. Terrain Textures
        string terrainDir = Path.Combine(baseDir, "assets", "config", "terrain");
        if (!Directory.Exists(terrainDir))
        {
            terrainDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "terrain"));
        }

        var textureDefs = new List<TerrainTextureDef>();
        if (Directory.Exists(terrainDir))
        {
            foreach (var file in Directory.GetFiles(terrainDir, "*.json"))
            {
                // Some files might be root world.json, skip if it doesn't look like a texture def
                try
                {
                    var def = JsonModelLoader.LoadFile<TerrainTextureDef>(file);
                    if (!string.IsNullOrEmpty(def.Id))
                    {
                        textureDefs.Add(def);
                    }
                }
                catch { /* skip non-texture JSON files */ }
            }
        }
        services.AddSingleton<ITerrainTextureRegistry>(new TerrainTextureRegistry(textureDefs));
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
