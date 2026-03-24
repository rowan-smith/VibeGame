using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// ECS system that manages adaptive terrain ring updates based on player/camera position and performance.
    /// </summary>
    public class TerrainUpdateSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly TerrainManager _terrainManager;

        public TerrainUpdateSystem(EntityRegistry entities, TerrainManager terrainManager)
        {
            _entities = entities;
            _terrainManager = terrainManager;
        }

        public void Update(float dt)
        {
            // Find the player/camera entity
            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                var cam = entity.GetComponent<CameraComponent>();
                // Use the adaptive update logic from TerrainManager
                _terrainManager.UpdateAround(cam.Position, 0);
            }
        }
    }
}
