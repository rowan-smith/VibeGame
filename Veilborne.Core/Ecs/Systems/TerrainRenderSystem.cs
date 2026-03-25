using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    public class TerrainRenderSystem : IRenderSystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private readonly ITerrainRenderer _renderer;

        public TerrainRenderSystem(EntityRegistry entities, IInfiniteTerrain terrain, ITerrainRenderer renderer)
        {
            _entities = entities;
            _terrain = terrain;
            _renderer = renderer;
        }

        public void Draw()
        {
            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                var cam = entity.GetComponent<CameraComponent>();
                _terrain.Render(cam);
            }
            _renderer.Flush();
        }
    }
}
