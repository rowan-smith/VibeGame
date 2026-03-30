using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Placeholder patch regeneration stage for dirty terrain patches.
    /// Current terrain renderer already rebuilds dirty regions via terrain services.
    /// </summary>
    public class PatchRegenSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;

        public PatchRegenSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            var toRemove = new List<Entity>();
            foreach (var entity in _entities.GetEntitiesWith<TerrainPatchDirtyComponent>())
                toRemove.Add(entity);

            foreach (var entity in toRemove)
                _entities.DestroyEntity(entity);
        }
    }
}
