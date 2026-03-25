using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Veilborne.Biomes;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Ecs.Systems;
using Veilborne.Core.MonoGameImpl;
using Veilborne.Core.Settings;
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
            var realRenderer = new MonoGameTerrainRenderer(graphicsDevice, settings, biomeProvider);
            _terrainRenderer = realRenderer;

            // Swap the DI proxy renderer to the real implementation
            var proxy = _serviceProvider.GetService<ProxyTerrainRenderer>();
            proxy?.SetInner(realRenderer);

            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice, settings, contentManager, Matrix.Identity, Matrix.Identity);
            _uiProvider = new MonoGameUiProvider();

            _systems.Add(_serviceProvider.GetRequiredService<PlayerSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<TerrainUpdateSystem>());

            _terrainRenderSystem = new TerrainRenderSystem(entityRegistry, terrain, realRenderer);
            _renderSystems.Add(_terrainRenderSystem);
            _renderSystems.Add(_worldObjectRenderer as IRenderSystem);
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
            var realRenderer = new MonoGameTerrainRenderer(graphicsDevice, settings, biomeProvider);
            _terrainRenderer = realRenderer;

            var proxy = _serviceProvider.GetService<ProxyTerrainRenderer>();
            proxy?.SetInner(realRenderer);

            _worldObjectRenderer = new MonoGameWorldObjectRenderer(entityRegistry, graphicsDevice, settings, null, Matrix.Identity, Matrix.Identity);
            _uiProvider = new MonoGameUiProvider();
            _systems.Add(_serviceProvider.GetRequiredService<PlayerSystem>());
            _systems.Add(_serviceProvider.GetRequiredService<TerrainUpdateSystem>());

            _terrainRenderSystem = new TerrainRenderSystem(entityRegistry, terrain, realRenderer);
            _renderSystems.Add(_terrainRenderSystem);
            _renderSystems.Add(_worldObjectRenderer as IRenderSystem);
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
