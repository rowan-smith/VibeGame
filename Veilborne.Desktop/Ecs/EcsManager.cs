using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Objects;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Desktop.MonoGameImpl;

namespace Veilborne.Desktop.Ecs
{
    /// <summary>
    /// Manages ECS systems initialization after MonoGame dependencies are available
    /// </summary>
    public class EcsManager : IEcsRuntime
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<ISystem> _systems = new();
        private readonly List<IRenderSystem> _renderSystems = new();
        private readonly EcsPerformanceMonitor? _perfMonitor;
        private readonly Stopwatch _systemTimer = new();
        
        // MonoGame-dependent services
        private IUiProvider? _uiProvider;
        private ITerrainRenderer? _terrainRenderer;
        private IWorldObjectRenderer? _worldObjectRenderer;
        private TerrainRenderSystem? _terrainRenderSystem;
        private ObjectRenderSystem? _objectRenderSystem;

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

            // Wire up input provider to the game so ShowCursor/HideCursor work
            if (game != null && _serviceProvider.GetService<IInputProvider>() is MonoGameInputProvider monoInput)
                monoInput.SetGame(game);

            // Use the game's built-in Content manager when available
            if (game?.Content != null)
            {
                Initialize(graphicsDevice, game.Content, entityRegistry, terrain);
            }
            else
            {
                InitializeWithoutContent(graphicsDevice, entityRegistry, terrain);
            }

            // Wire up the UI provider with the game's SpriteBatch so draw calls work
            if (spriteBatch == null)
                throw new InvalidOperationException("SpriteBatch is not initialized; UI cannot be constructed.");
            if (_uiProvider is MonoGameUiProvider uiProvider)
            {
                uiProvider.Initialize(spriteBatch, graphicsDevice);
            }
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
            _systems.Add(_serviceProvider.GetRequiredService<CleanupSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DependencySystem>());
            _systems.Add(_serviceProvider.GetRequiredService<InputSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DigInputSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DigProbeSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<VoxelRaycastSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<CameraSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<PlayerInputSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<HotbarSelectionSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DepleteSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DigExecutionSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DigParticleSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<PatchRegenSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AnimationSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ParticleSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomeDiscoverySystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AssetLoadSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomePrepSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ForceSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<IntegrationSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<WorldObjectSpatialIndexSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<CollisionDetectionSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<CollisionResolutionSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ConstraintSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<TerrainLoadQueueSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<TerrainLoadSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<TerrainGenSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<VegetationSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AssetUnloadSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ShadowMapSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<EffectSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<UISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DebugDrawSystem>());
        }

        private void RegisterRenderSystems(EntityRegistry entityRegistry, IInfiniteTerrain terrain, MonoGameTerrainRenderer realRenderer)
        {
            _terrainRenderSystem = new TerrainRenderSystem(entityRegistry, terrain, realRenderer);
            _objectRenderSystem = new ObjectRenderSystem(entityRegistry, _worldObjectRenderer);
            _renderSystems.Add(_terrainRenderSystem);
            _renderSystems.Add(_objectRenderSystem);
            _renderSystems.Add(_serviceProvider.GetRequiredService<CompositeRenderSystem>());
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
                renderSystem.Draw(); // IRenderSystem uses Draw(), not Render()
                _systemTimer.Stop();
                _perfMonitor?.RecordRender(renderSystem.GetType().Name, _systemTimer.Elapsed.TotalMilliseconds);
            }

            // Record terrain renderer metrics for the debug overlay
            if (_perfMonitor != null && _terrainRenderer is MonoGameTerrainRenderer mtr)
            {
                _perfMonitor.RecordCustomMetric("Chunks", mtr.LastChunksDrawn);
                _perfMonitor.RecordCustomMetric("DrawCalls", mtr.LastDrawCalls);
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
