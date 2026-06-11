using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Settings.Configuration;
using Veilborne;
using Veilborne.Biomes;
using Veilborne.Biomes.Environment;
using Veilborne.Biomes.Spawners;
using Veilborne.Camera;
using Veilborne.Ecs;
using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.Items;
using Veilborne.Objects;
using Veilborne.Settings;
using Veilborne.Sky;
using Veilborne.Stubs;
using Veilborne.Terrain;
using Veilborne.TerrainTexture;
using Veilborne.UI;
using Veilborne.WorldObjects;
using Veilborne.Web.MonoGameImpl;
using Veilborne.Web.WebImpl;

namespace Veilborne.Web;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        var options = new ConfigurationReaderOptions(
            typeof(Serilog.ConsoleLoggerConfigurationExtensions).Assembly);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .ReadFrom.Configuration(builder.Configuration, options)
            .WriteTo.BrowserConsole()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(_ => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        });

        await PreloadAndRegisterConfigurationsAsync(builder);

        RegisterCoreServices(builder.Services);

        builder.Services.AddSingleton<WebUiProvider>(sp => new WebUiProvider(
            sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WebUiProvider>>()));
        builder.Services.AddSingleton<IUiProvider>(sp => sp.GetRequiredService<WebUiProvider>());
        builder.Services.AddSingleton<IGraphicsProvider, WebGraphicsProvider>();
        builder.Services.AddSingleton<IGameLoopHost>(sp => new WebGameLoopHost(
            sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            sp.GetRequiredService<IUiProvider>()));
        builder.Services.AddSingleton<IInputProvider, WebInputProvider>();
        builder.Services.AddSingleton<IEcsRuntime>(sp => new WebEcsRuntime(
            sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            sp.GetRequiredService<IUiProvider>()));
        builder.Services.AddSingleton<IGameSettingsService, WebGameSettingsService>();
        builder.Services.AddSingleton<ISkyLightingService, WebSkyLightingService>();
        builder.Services.AddSingleton<ITimeService, WebTimeService>();

        builder.Services.AddSingleton<WebProxyTerrainRenderer>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => sp.GetRequiredService<WebProxyTerrainRenderer>());
        builder.Services.AddSingleton<IWorldObjectRenderer, StubWorldObjectRenderer>();

        var host = builder.Build();
        _ = StartEngineAsync(host);

        var uiProvider = host.Services.GetRequiredService<IUiProvider>() as WebUiProvider;
        uiProvider?.RegisterMenuAssets();

        await host.RunAsync();
    }

    private static void RegisterCoreServices(IServiceCollection services)
    {
        services.AddSingleton<CleanupSystem>();
        services.AddSingleton<DependencySystem>();
        services.AddSingleton<InputSystem>();
        services.AddSingleton<DigInputSystem>();
        services.AddSingleton<DigProbeSystem>();
        services.AddSingleton<VoxelRaycastSystem>();
        services.AddSingleton<IRandomSource, SystemRandomSource>();
        services.AddSingleton<DepleteSystem>();
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
        services.AddSingleton<EcsFrameContext>();
        services.AddSingleton<FrustumCullSystem>();
        services.AddSingleton<SortSystem>();
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

        services.AddSingleton<ITerrainGenerator>(sp =>
            new TerrainGenerator(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));
        services.AddSingleton<IShadowMapService, CpuShadowMapService>();

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
        services.AddSingleton<ITerrainStreaming>(sp => sp.GetRequiredService<TerrainManager>());

        services.AddSingleton<ICameraController, FpsCameraController>();
        services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        services.AddSingleton<HudUiController>();
        services.AddSingleton<DebugOverlayUiController>();
        services.AddSingleton<EntityRegistry>();
    }

    private static async Task PreloadAndRegisterConfigurationsAsync(WebAssemblyHostBuilder builder)
    {
        var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };

        var response = await http.GetAsync($"assets/config/world.json?v={DateTime.Now.Ticks}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to load world.json: {response.StatusCode}");

        var worldJson = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(worldJson))
            throw new InvalidOperationException("world.json is empty");

        var worldConfig = WebJsonUtils.LoadString<WorldConfig>(worldJson)
            ?? throw new InvalidOperationException("Failed to deserialize world.json");

        worldConfig.EditableRadius = 1;
        worldConfig.ReadOnlyRadius = 1;
        worldConfig.LowLodRadius = 0;
        worldConfig.TerrainLoadQueueRadius = 1;
        worldConfig.MaxActiveVoxelChunks = 32;
        worldConfig.TerrainRuntime.MaxEditableRadius = 1;
        worldConfig.TerrainRuntime.MinEditableRadius = 1;
        worldConfig.TerrainRuntime.MaxReadOnlyRadius = 1;
        worldConfig.TerrainRuntime.MinReadOnlyRadius = 1;
        worldConfig.TerrainRuntime.MaxLowLodRadius = 0;
        worldConfig.TerrainRuntime.MinLowLodRadius = 0;
        worldConfig.TerrainRuntime.MaxTerrainDrawDistance = 350f;
        worldConfig.TerrainRuntime.MaxMeshBuildsPerFrame = 2;
        worldConfig.TerrainRuntime.MaxReadOnlyConcurrentJobs = 2;
        worldConfig.TerrainRuntime.MaxLowLodConcurrentJobs = 2;
        Log.Information(
            "WASM terrain overrides: Editable={Editable}, ReadOnly={ReadOnly}, LowLod={LowLod}",
            worldConfig.EditableRadius, worldConfig.ReadOnlyRadius, worldConfig.LowLodRadius);

        builder.Services.AddSingleton<IWorldConfigService>(new WebWorldConfigService(worldConfig));

        var toolsJson = await http.GetStringAsync("assets/config/items/tools.json");
        var toolsSet = WebJsonUtils.LoadString<ItemConfigSet>(toolsJson)
            ?? throw new InvalidOperationException("Failed to deserialize tools.json");
        builder.Services.AddSingleton<IItemRegistry>(new WebItemRegistry(toolsSet));

        var foliageJson = await http.GetStringAsync("assets/config/world_objects/foliage.json");
        var treesJson = await http.GetStringAsync("assets/config/world_objects/trees.json");
        var foliageConfig = WebJsonUtils.LoadString<WorldObjectsConfig>(foliageJson);
        var treesConfig = WebJsonUtils.LoadString<WorldObjectsConfig>(treesJson);
        if (foliageConfig == null || treesConfig == null)
            throw new InvalidOperationException("Failed to deserialize world object configs");

        var woRegistry = new WebWorldObjectRegistry(new[] { foliageConfig, treesConfig });
        builder.Services.AddSingleton<IWorldObjectRegistry>(woRegistry);

        await RegisterWebBiomesAsync(builder.Services, http, woRegistry);
        await RegisterWebTerrainTexturesAsync(builder.Services, http);
    }

    private static async Task RegisterWebBiomesAsync(IServiceCollection services, HttpClient http, IWorldObjectRegistry woRegistry)
    {
        string[] biomeFiles = {
            "aetherwild_grove.json", "bloodpetal_wilds.json", "echostep_marsh.json",
            "emberroot_basin.json", "frostveil_tundra.json", "glimmerfall_ridge.json",
            "mistral_dunes.json", "nullscape.json", "obsidian_expanse.json",
            "shatterglass_desert.json", "solaris_steppe.json", "verdigris_expanse.json"
        };

        foreach (var file in biomeFiles)
        {
            try
            {
                var json = await http.GetStringAsync($"assets/config/biomes/{file}");
                var dto = WebJsonUtils.LoadString<BiomeData>(json);
                if (dto == null || !dto.Enabled) continue;

                services.AddSingleton<IBiome>(sp =>
                {
                    var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                    var envTerrain = sp.GetRequiredService<ITerrainGenerator>();
                    var config = sp.GetRequiredService<IWorldConfigService>();
                    return new ConfigBiome(dto.Id, dto,
                        new ConfigTreeWorldObjectSpawner(woRegistry, sampler, envTerrain, config, dto.AllowedObjects));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Program] Failed to load biome {file}: {ex.Message}");
            }
        }

        services.AddSingleton<IEnvironmentSampler>(sp =>
            new MultiNoiseSampler(sp.GetRequiredService<IWorldConfigService>().NoiseConfig));
    }

    private static async Task RegisterWebTerrainTexturesAsync(IServiceCollection services, HttpClient http)
    {
        string[] terrainFiles = {
            "aerial_rocks.json", "brown_mud.json", "brown_mud_leaves.json",
            "lichen_rock.json", "rock_3.json", "snow.json"
        };

        var defs = new List<TerrainTextureDef>();
        foreach (var file in terrainFiles)
        {
            try
            {
                var json = await http.GetStringAsync($"assets/config/terrain/{file}");
                var def = WebJsonUtils.LoadString<TerrainTextureDef>(json);
                if (def != null) defs.Add(def);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Program] Failed to load terrain texture {file}: {ex.Message}");
            }
        }

        services.AddSingleton<ITerrainTextureRegistry>(new WebTerrainTextureRegistry(defs));
    }

    private static async Task StartEngineAsync(WebAssemblyHost host)
    {
        try
        {
            using var scope = host.Services.CreateScope();
            var services = scope.ServiceProvider;
            var engine = new VeilborneEngine(
                services.GetRequiredService<IInfiniteTerrain>(),
                services.GetRequiredService<ITerrainStreaming>(),
                services.GetRequiredService<ITimeService>(),
                services.GetRequiredService<EntityRegistry>(),
                services.GetRequiredService<IGraphicsProvider>(),
                services.GetRequiredService<IGameLoopHost>(),
                services.GetRequiredService<IInputProvider>(),
                services.GetRequiredService<IEcsRuntime>(),
                services.GetRequiredService<IGameSettingsService>(),
                services.GetRequiredService<ISkyLightingService>(),
                services.GetRequiredService<HudUiController>(),
                services.GetRequiredService<DebugOverlayUiController>(),
                services.GetRequiredService<EcsPerformanceMonitor>());
            await engine.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] CRITICAL ERROR starting engine: {ex}");
        }
    }
}
