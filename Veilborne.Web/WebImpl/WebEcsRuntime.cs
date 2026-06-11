using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.Objects;
using Veilborne.Stubs;

namespace Veilborne.Web.WebImpl;

public sealed class WebEcsRuntime : IEcsRuntime
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUiProvider _uiProvider;
    private readonly WebProxyTerrainRenderer _terrainProxy;
    private readonly List<ISystem> _systems = new();
    private readonly List<IRenderSystem> _renderSystems = new();
    private readonly EcsPerformanceMonitor? _perfMonitor;
    private readonly Stopwatch _systemTimer = new();
    private readonly StubWorldObjectRenderer _worldObjectRenderer = new();

    private WebPixiTerrainRenderer? _terrainRenderer;

    public WebEcsRuntime(
        IServiceProvider serviceProvider,
        IUiProvider uiProvider,
        WebProxyTerrainRenderer terrainProxy)
    {
        _serviceProvider = serviceProvider;
        _uiProvider = uiProvider;
        _terrainProxy = terrainProxy;
        _perfMonitor = serviceProvider.GetService<EcsPerformanceMonitor>();
    }

    public void Initialize(EntityRegistry entityRegistry, IInfiniteTerrain terrain)
    {
        _terrainRenderer = new WebPixiTerrainRenderer(
            _serviceProvider.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
            _serviceProvider.GetRequiredService<IGraphicsProvider>(),
            _serviceProvider.GetRequiredService<WebTerrainProceduralTextures>());
        _terrainProxy.SetInner(_terrainRenderer);

        _systems.Clear();
        _systems.AddRange(EcsSystemPipeline.BuildUpdatePipeline(_serviceProvider));

        _renderSystems.Clear();
        _renderSystems.AddRange(EcsRenderSystemPipeline.Build(
            entityRegistry,
            terrain,
            _terrainRenderer,
            _worldObjectRenderer));
    }

    public void UpdateSystems(float deltaTime)
    {
        _perfMonitor?.BeginFrame();
        foreach (var system in _systems)
        {
            _systemTimer.Restart();
            system.Update(deltaTime);
            _systemTimer.Stop();
            _perfMonitor?.RecordUpdate(system.GetType().Name, _systemTimer.Elapsed.TotalMilliseconds);
        }
    }

    public void RenderSystems(float deltaTime, CameraComponent camera)
    {
        foreach (var renderSystem in _renderSystems)
        {
            _systemTimer.Restart();
            renderSystem.Draw();
            _systemTimer.Stop();
            _perfMonitor?.RecordRender(renderSystem.GetType().Name, _systemTimer.Elapsed.TotalMilliseconds);
        }
    }

    public IUiProvider GetUiProvider() => _uiProvider;

    public ITerrainRenderer GetTerrainRenderer() =>
        _terrainRenderer ?? throw new InvalidOperationException("Terrain renderer not initialized.");

    public IWorldObjectRenderer GetWorldObjectRenderer() => _worldObjectRenderer;
}
