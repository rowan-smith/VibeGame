using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Updates terrain streaming rings around the active camera.
    /// </summary>
    public class TerrainLoadSystem : ISystem
    {
        private readonly TerrainLoadRequestTracker _tracker;
        private readonly EntityRegistry _entities;

        public TerrainLoadSystem(EntityRegistry entities, TerrainLoadRequestTracker tracker)
        {
            _entities = entities;
            _tracker = tracker;
        }

        public void Update(float dt)
        {
            // Drain all stale request entities in one pass; streaming is now driven by TerrainLoadQueueSystem.
            foreach (var requestEntity in _entities.GetEntitiesWith<TerrainLoadRequestComponent>())
            {
                var req = requestEntity.GetComponent<TerrainLoadRequestComponent>();
                _tracker.Dequeue((req.ChunkX, req.ChunkZ));
                _entities.DestroyEntity(requestEntity);
            }
        }
    }
}
