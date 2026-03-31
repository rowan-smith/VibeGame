using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Serilog;
using Serilog.Settings.Configuration;
using Veilborne.Web.MonoGameImpl;
using Veilborne.Core;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Core.UI;
using Veilborne.Core.Ecs;
using Veilborne.Core.Objects;
using Veilborne.Core.Items;
using Veilborne.Core.Camera;
using Veilborne.Core.Stubs;
using Veilborne.Web.WebImpl;
using Veilborne.Core.Biomes;
using Veilborne.Core.Biomes.Spawners;
using Veilborne.Core.TerrainTexture;
using Veilborne.Core.WorldObjects;

namespace Veilborne.Web;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        var options = new ConfigurationReaderOptions(
            typeof(Serilog.ConsoleLoggerConfigurationExtensions).Assembly);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .ReadFrom.Configuration(builder.Configuration, options)
            .WriteTo.BrowserConsole()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Serilog initialized in Blazor WASM. BrowserConsole and Console sinks active.");

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped(_ => new HttpClient
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        });

        builder.Services.AddVeilborneCoreServices();

        // 2. Register Web-specific implementations as overrides
        builder.Services.AddSingleton<IGraphicsProvider, WebGraphicsProvider>();
        builder.Services.AddSingleton<IGameLoopHost, WebGameLoopHost>();
        builder.Services.AddSingleton<IUiProvider>(sp => new WebUiProvider(
            sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WebUiProvider>>()
        ));
        builder.Services.AddSingleton<IInputProvider, WebInputProvider>();
        builder.Services.AddSingleton<IEcsRuntime>(sp => new WebEcsRuntime(
            sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            sp.GetRequiredService<IUiProvider>()
        ));
        builder.Services.AddSingleton<IGameSettingsService, WebGameSettingsService>();
        builder.Services.AddSingleton<ISkyLightingService, WebSkyLightingService>();
        builder.Services.AddSingleton<ITimeService, WebTimeService>();

        // Pre-load all required configuration from assets via HttpClient and register overrides
        await PreloadAndRegisterConfigurationsAsync(builder);

        // Register implementations for core interface overrides/missing services
        builder.Services.AddSingleton<ICameraController, FpsCameraController>();
        builder.Services.AddSingleton<IPhysicsController, SimplePhysicsController>();
        
        // Use Stub for 3D renderers for now, Proxy for Terrain
        builder.Services.AddSingleton<ITerrainRenderer, StubTerrainRenderer>();
        builder.Services.AddSingleton<IWorldObjectRenderer, StubWorldObjectRenderer>();

        // Use WebProxyTerrainRenderer and ensure it's the one registered for ITerrainRenderer
        builder.Services.AddSingleton<WebProxyTerrainRenderer>();
        builder.Services.AddSingleton<ITerrainRenderer>(sp => sp.GetRequiredService<WebProxyTerrainRenderer>());

        // Register GameMenuRenderer
        builder.Services.AddSingleton<GameMenuRenderer>(sp =>
        {
            var settings = sp.GetRequiredService<IGameSettingsService>();
            var input = sp.GetRequiredService<IInputProvider>();
            var isDevelopment = builder.HostEnvironment.IsDevelopment();
            var renderer = new GameMenuRenderer(settings, input, isDevelopment);
            renderer.Initialize(sp.GetRequiredService<IUiProvider>(), sp.GetRequiredService<IGraphicsProvider>());
            return renderer;
        });

        var host = builder.Build();
        // After Blazor is ready, start the game engine
        _ = StartEngineAsync(host);

        // Register menu/game assets for the web UI
        var uiProvider = host.Services.GetRequiredService<IUiProvider>() as WebUiProvider;
        uiProvider?.RegisterMenuAssets();
        
        await host.RunAsync();
    }

    private static async Task PreloadAndRegisterConfigurationsAsync(WebAssemblyHostBuilder builder)
    {
        var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };

        // 1. Fetch world.json
        string worldJson = "";
        try
        {
            var url = $"assets/config/world.json?v={DateTime.Now.Ticks}";
            Console.WriteLine($"[Program] Fetching {url} (BaseAddress: {http.BaseAddress})");
            var response = await http.GetAsync(url);
            
            Console.WriteLine($"[Program] Response for world.json: {response.StatusCode}, Content-Length: {response.Content.Headers.ContentLength}, Content-Type: {response.Content.Headers.ContentType}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Server returned status code {response.StatusCode} for world.json. Body: {(errorBody.Length > 200 ? errorBody[..200] : errorBody)}");
            }

            worldJson = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(worldJson))
            {
                throw new InvalidOperationException($"world.json is empty. Length: {worldJson.Length}");
            }
            
            if (worldJson.Trim().StartsWith("<"))
            {
                throw new InvalidOperationException($"world.json seems to be HTML instead of JSON. Preview: {(worldJson.Length > 100 ? worldJson[..100] : worldJson)}");
            }

            var worldConfig = WebJsonUtils.LoadString<WorldConfig>(worldJson);
            if (worldConfig == null) throw new InvalidOperationException("Failed to deserialize world.json");

            // Override radii for WASM to ensure significantly faster initial loading
            // and more cooperative memory/CPU usage in the single-threaded browser environment.
            worldConfig.LowLodRadius = 6; 
            worldConfig.ReadOnlyRadius = 3;
            worldConfig.TerrainRuntime.MaxLowLodRadius = 8;
            worldConfig.TerrainRuntime.MaxReadOnlyRadius = 5;
            Log.Information("WASM Radius Overrides: LowLod={LowLod}, ReadOnly={ReadOnly}", worldConfig.LowLodRadius, worldConfig.ReadOnlyRadius);

            builder.Services.AddSingleton<IWorldConfigService>(new WorldConfigService(worldConfig));
        }
        catch (Exception ex)
        {
            var preview = string.IsNullOrEmpty(worldJson) ? "EMPTY" : (worldJson.Length > 100 ? worldJson[..100] : worldJson);
            Console.WriteLine($"[Program] Failed to load world.json: {ex.Message}");
            throw;
        }

        // 2. Fetch all item config sets
        try
        {
            var toolsJson = await http.GetStringAsync("assets/config/items/tools.json");
            var toolsSet = WebJsonUtils.LoadString<ItemConfigSet>(toolsJson);
            if (toolsSet == null) throw new InvalidOperationException("Failed to deserialize tools.json");
            builder.Services.AddSingleton<IItemRegistry>(new ItemRegistry(new[] { toolsSet }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] Failed to load item configs: {ex.Message}");
            throw;
        }

        // 3. Fetch all world object configs
        try
        {
            var foliageJson = await http.GetStringAsync("assets/config/world_objects/foliage.json");
            var foliageConfig = WebJsonUtils.LoadString<WorldObjectsConfig>(foliageJson);
            var treesJson = await http.GetStringAsync("assets/config/world_objects/trees.json");
            var treesConfig = WebJsonUtils.LoadString<WorldObjectsConfig>(treesJson);
            if (foliageConfig == null || treesConfig == null) throw new InvalidOperationException("Failed to deserialize world object configs");
            var woRegistry = new WorldObjectRegistry(new[] { foliageConfig, treesConfig });
            builder.Services.AddSingleton<IWorldObjectRegistry>(woRegistry);

            // 4. Fetch and register biomes
            await RegisterWebBiomesAsync(builder.Services, http, woRegistry);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] Failed to load world object configs: {ex.Message}");
            throw;
        }

        // 5. Fetch and register terrain textures
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
                if (dto == null) continue;
                if (!dto.Enabled) continue;

                services.AddSingleton<IBiome>(sp =>
                {
                    var sampler = sp.GetRequiredService<IEnvironmentSampler>();
                    var envTerrain = sp.GetRequiredService<ITerrainGenerator>();
                    var config = sp.GetRequiredService<IWorldConfigService>();
                    return new ConfigBiome(dto.Id, dto, new ConfigTreeWorldObjectSpawner(woRegistry, sampler, envTerrain, config, dto.AllowedObjects));
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Program] Failed to load biome {file}: {ex.Message}");
            }
        }
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

        services.AddSingleton<ITerrainTextureRegistry>(new TerrainTextureRegistry(defs));
    }
    private static async Task StartEngineAsync(WebAssemblyHost host)
    {
        try
        {
            Console.WriteLine("[Program] Starting VeilborneEngine...");
            using var scope = host.Services.CreateScope();
            var services = scope.ServiceProvider;
            var engine = new VeilborneEngine(
                services.GetRequiredService<IInfiniteTerrain>(),
                services.GetRequiredService<ITimeService>(),
                services.GetRequiredService<EntityRegistry>(),
                services.GetRequiredService<IGraphicsProvider>(),
                services.GetRequiredService<IGameLoopHost>(),
                services.GetRequiredService<IInputProvider>(),
                services.GetRequiredService<IEcsRuntime>(),
                services.GetRequiredService<IGameSettingsService>(),
                services.GetRequiredService<ISkyLightingService>(),
                services.GetRequiredService<HudUiController>(),
                services.GetRequiredService<DebugOverlayUiController>()
            );
            await engine.RunAsync();
            Console.WriteLine("[Program] VeilborneEngine.RunAsync() completed (Task.CompletedTask).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Program] CRITICAL ERROR starting engine: {ex}");
        }
    }
}
