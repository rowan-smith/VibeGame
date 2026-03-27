using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Veilborne.Biomes;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.MonoGameImpl;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Interfaces;
using Veilborne.Objects;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Veilborne.Core.Ecs
{
    /// <summary>
    /// Manages ECS systems initialization after MonoGame dependencies are available
    /// </summary>
    public class EcsManager : IEcsRuntime
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly List<ISystem> _systems = new();
        private readonly List<IRenderSystem> _renderSystems = new();
        
        // MonoGame-dependent services
        private IUiProvider _uiProvider;
        private ITerrainRenderer _terrainRenderer;
        private IWorldObjectRenderer _worldObjectRenderer;
        private TerrainRenderSystem _terrainRenderSystem;

        public EcsManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Initialize(GraphicsDevice graphicsDevice, ContentManager contentManager, EntityRegistry entityRegistry, IInfiniteTerrain terrain)
        {
            var biomeProvider = _serviceProvider.GetService<IBiomeProvider>();
            var settings = _serviceProvider.GetRequiredService<IGameSettingsService>();
            var sky = _serviceProvider.GetRequiredService<ISkyLightingService>();
            var shadowMap = _serviceProvider.GetRequiredService<IShadowMapService>();
            var realRenderer = new MonoGameTerrainRenderer(graphicsDevice, settings, sky, shadowMap, biomeProvider);
            _terrainRenderer = realRenderer;

            // Swap the DI proxy renderer to the real implementation
            var proxy = _serviceProvider.GetService<ProxyTerrainRenderer>();
            proxy?.SetInner(realRenderer);

            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice, settings, sky, shadowMap, contentManager, Matrix.Identity, Matrix.Identity);
            _uiProvider = new MonoGameUiProvider();

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
            _systems.Add(_serviceProvider.GetRequiredService<PatchRegenSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AnimationSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ParticleSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomeDiscoverySystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AssetLoadSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomePrepSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ForceSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<IntegrationSystem>());
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
            _systems.Add(_serviceProvider.GetRequiredService<FrustumCullSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<SortSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<UISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DebugDrawSystem>());

            _terrainRenderSystem = new TerrainRenderSystem(entityRegistry, terrain, realRenderer);
            _renderSystems.Add(_terrainRenderSystem);
            _renderSystems.Add(_worldObjectRenderer as IRenderSystem);
            _renderSystems.Add(_serviceProvider.GetRequiredService<CompositeRenderSystem>());
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
            var biomeProvider = _serviceProvider.GetService<IBiomeProvider>();
            var settings = _serviceProvider.GetRequiredService<IGameSettingsService>();
            var sky = _serviceProvider.GetRequiredService<ISkyLightingService>();
            var shadowMap = _serviceProvider.GetRequiredService<IShadowMapService>();
            var realRenderer = new MonoGameTerrainRenderer(graphicsDevice, settings, sky, shadowMap, biomeProvider);
            _terrainRenderer = realRenderer;

            var proxy = _serviceProvider.GetService<ProxyTerrainRenderer>();
            proxy?.SetInner(realRenderer);

            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice, settings, sky, shadowMap, null, Matrix.Identity, Matrix.Identity);
            _uiProvider = new MonoGameUiProvider();
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
            _systems.Add(_serviceProvider.GetRequiredService<PatchRegenSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AnimationSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ParticleSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomeDiscoverySystem>());
            _systems.Add(_serviceProvider.GetRequiredService<AssetLoadSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<BiomePrepSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<ForceSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<IntegrationSystem>());
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
            _systems.Add(_serviceProvider.GetRequiredService<FrustumCullSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<SortSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<UISystem>());
            _systems.Add(_serviceProvider.GetRequiredService<DebugDrawSystem>());

            _terrainRenderSystem = new TerrainRenderSystem(entityRegistry, terrain, realRenderer);
            _renderSystems.Add(_terrainRenderSystem);
            _renderSystems.Add(_worldObjectRenderer as IRenderSystem);
            _renderSystems.Add(_serviceProvider.GetRequiredService<CompositeRenderSystem>());
        }

        public void UpdateSystems(float deltaTime)
        {
            foreach (var system in _systems)
            {
                system.Update(deltaTime);
            }
        }

        public void RenderSystems(float deltaTime, CameraComponent camera)
        {
            foreach (var renderSystem in _renderSystems)
            {
                renderSystem.Draw(); // IRenderSystem uses Draw(), not Render()
            }
        }

        public IUiProvider GetUiProvider() => _uiProvider;
        public ITerrainRenderer GetTerrainRenderer() => _terrainRenderer;
        public IWorldObjectRenderer GetWorldObjectRenderer() => _worldObjectRenderer;
    }
}
