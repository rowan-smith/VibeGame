using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Loads biome asset references lazily and marks bundles as loaded.
    /// </summary>
    public class AssetLoadSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly BiomeAssetTracker _tracker;

        public AssetLoadSystem(EntityRegistry entities, BiomeAssetTracker tracker)
        {
            _entities = entities;
            _tracker = tracker;
        }

        public void Update(float dt)
        {
            foreach (var reqEntity in _entities.GetEntitiesWith<BiomeLoadRequestComponent>())
            {
                var req = reqEntity.GetComponent<BiomeLoadRequestComponent>();
                if (string.IsNullOrWhiteSpace(req.BiomeId))
                {
                    _entities.DestroyEntity(reqEntity);
                    continue;
                }

                var bundle = _entities.CreateEntity();
                bundle.AddComponent(new AssetBundleComponent
                {
                    BiomeId = req.BiomeId,
                    IsLoaded = true
                });
                bundle.AddComponent(new BiomeLoadedAssetsComponent
                {
                    BiomeId = req.BiomeId,
                    GrassTexturePath = $"assets\\textures\\{req.BiomeId}_grass.png",
                    TreeModelPath = $"assets\\models\\tree_oak.glb",
                    ActiveChunkRefs = 1
                });

                _tracker.Requested.Remove(req.BiomeId);
                _tracker.Loaded.Add(req.BiomeId);
                _entities.DestroyEntity(reqEntity);
            }
        }
    }
}

