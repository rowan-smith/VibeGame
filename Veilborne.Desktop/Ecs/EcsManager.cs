using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.MonoGameImpl;
using Veilborne.Objects;
using Veilborne.Settings;
using Veilborne.Sky;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Manages ECS systems initialization after MonoGame dependencies are available.
    /// </summary>
    public class EcsManager : IEcsRuntime
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<ISystem> _systems = new();
        private readonly List<IRenderSystem> _renderSystems = new();
        private readonly EcsPerformanceMonitor? _perfMonitor;
        private readonly Stopwatch _systemTimer = new();

        private IUiProvider? _uiProvider;
        private ITerrainRenderer? _terrainRenderer;
        private IWorldObjectRenderer? _worldObjectRenderer;

        public EcsManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _perfMonitor = serviceProvider.GetService<EcsPerformanceMonitor>();
        }

        public void Initialize(GraphicsDevice graphicsDevice, ContentManager contentManager, EntityRegistry entityRegistry, IInfiniteTerrain terrain)
        {
            var (realRenderer, worldConfig) = InitializeRenderers(graphicsDevice, contentManager);
            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice,
                _serviceProvider.GetRequiredService<IGameSettingsService>(),
                _serviceProvider.GetRequiredService<ISkyLightingService>(),
                _serviceProvider.GetRequiredService<IShadowMapService>(),
                contentManager, Matrix.Identity, Matrix.Identity, worldConfig);
            _uiProvider = new MonoGameUiProvider();

            RegisterSystems();
            RegisterRenderSystems(entityRegistry, terrain, realRenderer);
        }

        public void Initialize(EntityRegistry entityRegistry, IInfiniteTerrain terrain)
        {
            var graphicsProvider = _serviceProvider.GetRequiredService<IGraphicsProvider>();
            if (graphicsProvider is not MonoGameGraphicsProvider monoGameProvider)
                throw new InvalidOperationException("ECS requires a MonoGameGraphicsProvider for initialization.");
            var graphicsDevice = monoGameProvider.GetGraphicsDevice() ?? throw new InvalidOperationException("GraphicsDevice is not available; MonoGame may not be initialized yet.");
            var spriteBatch = monoGameProvider.GetSpriteBatch();
            var game = monoGameProvider.GetGame();

            if (game != null && _serviceProvider.GetService<IInputProvider>() is MonoGameInputProvider monoInput)
                monoInput.SetGame(game);

            if (game?.Content != null)
                Initialize(graphicsDevice, game.Content, entityRegistry, terrain);
            else
                InitializeWithoutContent(graphicsDevice, entityRegistry, terrain);

            if (spriteBatch == null)
                throw new InvalidOperationException("SpriteBatch is not initialized; UI cannot be constructed.");
            if (_uiProvider is MonoGameUiProvider uiProvider)
                uiProvider.Initialize(spriteBatch, graphicsDevice);
        }

        private void InitializeWithoutContent(GraphicsDevice graphicsDevice, EntityRegistry entityRegistry, IInfiniteTerrain terrain)
        {
            var (realRenderer, worldConfig) = InitializeRenderers(graphicsDevice);
            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice,
                _serviceProvider.GetRequiredService<IGameSettingsService>(),
                _serviceProvider.GetRequiredService<ISkyLightingService>(),
                _serviceProvider.GetRequiredService<IShadowMapService>(),
                null, Matrix.Identity, Matrix.Identity, worldConfig);
            _uiProvider = new MonoGameUiProvider();

            RegisterSystems();
            RegisterRenderSystems(entityRegistry, terrain, realRenderer);
        }

        private (MonoGameTerrainRenderer renderer, IWorldConfigService worldConfig) InitializeRenderers(
            GraphicsDevice graphicsDevice, ContentManager? contentManager = null)
        {
            var biomeProvider = _serviceProvider.GetService<IBiomeProvider>();
            var settings = _serviceProvider.GetRequiredService<IGameSettingsService>();
            var sky = _serviceProvider.GetRequiredService<ISkyLightingService>();
            var shadowMap = _serviceProvider.GetRequiredService<IShadowMapService>();
            var worldConfig = _serviceProvider.GetRequiredService<IWorldConfigService>();
            var realRenderer = new MonoGameTerrainRenderer(graphicsDevice, settings, sky, shadowMap, biomeProvider, worldConfig);
            _terrainRenderer = realRenderer;

            var proxy = _serviceProvider.GetService<ProxyTerrainRenderer>();
            proxy?.SetInner(realRenderer);

            return (realRenderer, worldConfig);
        }

        private void RegisterSystems()
        {
            _systems.Clear();
            _systems.AddRange(EcsSystemPipeline.BuildUpdatePipeline(_serviceProvider));
        }

        private void RegisterRenderSystems(EntityRegistry entityRegistry, IInfiniteTerrain terrain, MonoGameTerrainRenderer realRenderer)
        {
            _renderSystems.Clear();
            _renderSystems.AddRange(EcsRenderSystemPipeline.Build(
                entityRegistry,
                terrain,
                realRenderer,
                _worldObjectRenderer!));
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

            if (_perfMonitor != null && _terrainRenderer is MonoGameTerrainRenderer mtr)
            {
                _perfMonitor.RecordCustomMetric("Chunks", mtr.LastChunksDrawn);
                _perfMonitor.RecordCustomMetric("DrawCalls", mtr.LastDrawCalls);
                _perfMonitor.RecordCustomMetric("TexBatches", mtr.LastTextureBatches);
                _perfMonitor.RecordCustomMetric("EffectApply", mtr.LastEffectApplies);
                _perfMonitor.RecordCustomMetric("2ndPass", mtr.LastSecondaryPasses);
                _perfMonitor.RecordCustomMetric("MeshBuilds", mtr.LastMeshBuilds);
                _perfMonitor.RecordCustomMetric("Cached", mtr.TotalCachedChunks);
                _perfMonitor.RecordCustomMetric("Evicted", mtr.LastEvicted);
            }
        }

        public IUiProvider GetUiProvider() => _uiProvider ?? throw new InvalidOperationException("UI provider not initialized.");
        public ITerrainRenderer GetTerrainRenderer() => _terrainRenderer ?? throw new InvalidOperationException("Terrain renderer not initialized.");
        public IWorldObjectRenderer GetWorldObjectRenderer() => _worldObjectRenderer ?? throw new InvalidOperationException("World object renderer not initialized.");
    }
}
