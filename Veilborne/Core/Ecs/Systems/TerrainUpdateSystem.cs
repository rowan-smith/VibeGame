using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    public class TerrainUpdateSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;

        public TerrainUpdateSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                var cam = entity.GetComponent<CameraComponent>();
                _terrain.UpdateCenter(cam.Position);
            }
        }
    }
}
