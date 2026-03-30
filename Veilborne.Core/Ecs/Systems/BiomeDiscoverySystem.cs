using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Discovers active biomes from terrain chunks and queues asset load requests.
    /// </summary>
    public class BiomeDiscoverySystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly BiomeAssetTracker _tracker;
        private readonly HashSet<string> _active = new(System.StringComparer.OrdinalIgnoreCase);
        private int _frameCounter;

        public BiomeDiscoverySystem(EntityRegistry entities, BiomeAssetTracker tracker)
        {
            _entities = entities;
            _tracker = tracker;
        }

        public void Update(float dt)
        {
            // Only run every 3rd frame — biome chunks don't change rapidly
            if (++_frameCounter % 3 != 0)
                return;

            _active.Clear();
            foreach (var chunkEntity in _entities.GetEntitiesWith<TerrainChunkComponent, BiomeComponent>())
            {
                var biome = chunkEntity.GetComponent<BiomeComponent>();
                if (string.IsNullOrWhiteSpace(biome.BiomeId))
                    continue;
                _active.Add(biome.BiomeId);
            }

            foreach (var biomeId in _active)
            {
                _tracker.ActiveChunkRefs[biomeId] = _tracker.ActiveChunkRefs.TryGetValue(biomeId, out var refs) ? refs + 1 : 1;
                if (_tracker.Loaded.Contains(biomeId) || _tracker.Requested.Contains(biomeId))
                    continue;

                _tracker.Requested.Add(biomeId);
                var req = _entities.CreateEntity();
                req.AddComponent(new BiomeLoadRequestComponent
                {
                    BiomeId = biomeId,
                    Priority = 0
                });
            }
        }
    }
}

