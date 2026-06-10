using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Discovers active biomes from terrain chunks and queues asset load requests.
    /// </summary>
    public class BiomeDiscoverySystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly BiomeAssetTracker _tracker;
        private readonly Dictionary<string, int> _activeCounts = new(System.StringComparer.OrdinalIgnoreCase);
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

            _activeCounts.Clear();
            _entities.ForEachWith<TerrainChunkComponent, BiomeComponent>(entity =>
            {
                var biome = entity.GetComponent<BiomeComponent>();
                if (string.IsNullOrWhiteSpace(biome.BiomeId))
                    return;

                _activeCounts[biome.BiomeId] = _activeCounts.TryGetValue(biome.BiomeId, out var count) ? count + 1 : 1;
            });

            _tracker.ActiveChunkRefs.Clear();
            foreach (var (biomeId, count) in _activeCounts)
            {
                _tracker.ActiveChunkRefs[biomeId] = count;
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
