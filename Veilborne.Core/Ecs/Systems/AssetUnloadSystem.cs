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

        public AssetUnloadSystem(EntityRegistry entities, BiomeAssetTracker tracker)
        {
            _entities = entities;
            _tracker = tracker;
        }

        public void Update(float dt)
        {
            foreach (var bundle in _entities.GetEntitiesWith<BiomeLoadedAssetsComponent>())
            {
                var loaded = bundle.GetComponent<BiomeLoadedAssetsComponent>();
                if (_tracker.ActiveChunkRefs.TryGetValue(loaded.BiomeId, out var refs) && refs > 0)
                    continue;

                _tracker.Loaded.Remove(loaded.BiomeId);
                _entities.DestroyEntity(bundle);
            }

            _tracker.ActiveChunkRefs.Clear();
        }
    }
}

