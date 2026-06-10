using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Unloads biome bundles when no active chunk references remain.
    /// </summary>
    public class AssetUnloadSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly BiomeAssetTracker _tracker;
        private readonly List<Entity> _toUnload = new();

        public AssetUnloadSystem(EntityRegistry entities, BiomeAssetTracker tracker)
        {
            _entities = entities;
            _tracker = tracker;
        }

        public void Update(float dt)
        {
            _toUnload.Clear();
            _entities.ForEachWith<BiomeLoadedAssetsComponent>(entity =>
            {
                var loaded = entity.GetComponent<BiomeLoadedAssetsComponent>();
                if (_tracker.ActiveChunkRefs.TryGetValue(loaded.BiomeId, out var refs) && refs > 0)
                    return;

                _toUnload.Add(entity);
            });

            foreach (var bundle in _toUnload)
            {
                var loaded = bundle.GetComponent<BiomeLoadedAssetsComponent>();
                _tracker.Loaded.Remove(loaded.BiomeId);
                _entities.DestroyEntity(bundle);
            }
        }
    }
}
