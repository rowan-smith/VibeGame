using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Placeholder patch regeneration stage for dirty terrain patches.
    /// Current terrain renderer already rebuilds dirty regions via terrain services.
    /// </summary>
    public class PatchRegenSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly List<Entity> _toRemove = new();

        public PatchRegenSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            _toRemove.Clear();
            _entities.ForEachWith<TerrainPatchDirtyComponent>(entity => _toRemove.Add(entity));

            foreach (var entity in _toRemove)
                _entities.DestroyEntity(entity);
        }
    }
}
