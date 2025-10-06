using System.Numerics;
using VibeGame.Biomes.Environment;
using VibeGame.Core.WorldObjects;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    /// Spawner that uses biome AllowedObjects (tree ids) and trees registry.
    /// It places simple parametric trees (current renderer) while honoring basic spawn rules
    /// like elevation/temperature/moisture ranges. BiomeIds in the tree's SpawnRules are ignored
    /// when AllowedObjects are explicitly set on the biome.
    public sealed class AllowedObjectsTreeSpawner : ITreeSpawner
    {
        private readonly ITreesRegistry _trees;
        private readonly IEnvironmentSampler _sampler;
        private readonly IReadOnlyList<string> _allowedIds;

        public AllowedObjectsTreeSpawner(IReadOnlyList<string> allowedObjectIds, ITreesRegistry trees, IEnvironmentSampler sampler)
        {
            _allowedIds = allowedObjectIds;
            _trees = trees;
            _sampler = sampler;
        }

        public List<(string treeId, Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            Vector2 originWorld,
            int chunkSize,
            int targetCount)
        {
            var list = new List<(string treeId, Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>();
            if (_allowedIds.Count == 0) return list;

            float tile = terrain.TileSize;
            float chunkWorldSize = (chunkSize - 1) * tile;

            float margin = MathF.Max(2f * tile, 3f);
            float minX = originWorld.X + margin;
            float maxX = originWorld.X + chunkWorldSize - margin;
            float minZ = originWorld.Y + margin;
            float maxZ = originWorld.Y + chunkWorldSize - margin;

            // Simple even distribution of targetCount across allowed object ids
            int perType = Math.Max(1, targetCount / _allowedIds.Count);
            int seedBase = HashCode.Combine((int)originWorld.X, (int)originWorld.Y, chunkSize, 7919);
            int globalIdx = 0;

            foreach (var id in _allowedIds)
            {
                if (!_trees.TryGet(id, out var def)) continue;
                var sr = def.SpawnRules ?? new SpawnRulesConfig();
                float altMin = sr.AltitudeRange != null && sr.AltitudeRange.Length > 0 ? sr.AltitudeRange[0] : 0f;
                float altMax = sr.AltitudeRange != null && sr.AltitudeRange.Length > 1 ? sr.AltitudeRange[1] : 1f;
                float tMin = sr.TemperatureRange != null && sr.TemperatureRange.Length > 0 ? sr.TemperatureRange[0] : 0f;
                float tMax = sr.TemperatureRange != null && sr.TemperatureRange.Length > 1 ? sr.TemperatureRange[1] : 1f;
                float mMin = sr.MoistureRange != null && sr.MoistureRange.Length > 0 ? sr.MoistureRange[0] : 0f;
                float mMax = sr.MoistureRange != null && sr.MoistureRange.Length > 1 ? sr.MoistureRange[1] : 1f;

                for (int i = 0; i < perType; i++)
                {
                    int seed = HashCode.Combine(seedBase, id.GetHashCode(StringComparison.OrdinalIgnoreCase), i);
                    float wx = HashToRange(seed * 97 + 5, minX, maxX);
                    float wz = HashToRange(seed * 211 + 23, minZ, maxZ);

                    float baseY = terrain.ComputeHeight(wx, wz);

                    // Avoid steep slopes
                    float s = 1.5f;
                    float ny1 = terrain.ComputeHeight(wx + s, wz);
                    float ny2 = terrain.ComputeHeight(wx - s, wz);
                    float ny3 = terrain.ComputeHeight(wx, wz + s);
                    float ny4 = terrain.ComputeHeight(wx, wz - s);
                    float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)), MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
                    if (slope > 2.0f) continue;

                    // Environment filter
                    var env = _sampler.Sample(new Vector2(wx, wz), terrain);
                    if (env.Elevation < altMin || env.Elevation > altMax) continue;
                    if (env.Temperature < tMin || env.Temperature > tMax) continue;
                    if (env.Moisture < mMin || env.Moisture > mMax) continue;

                    // Parametric size: vary by id-based seed
                    float trunkHeight = 2.4f + HashToRange(seed * 17 + 11, 0.8f, 3.6f);
                    float trunkRadius = 0.20f + HashToRange(seed * 37 + 19, -0.03f, 0.14f);
                    float canopyRadius = trunkHeight * HashToRange(seed * 41 + 29, 0.45f, 0.72f);

                    list.Add((id, new Vector3(wx, baseY, wz), trunkHeight, trunkRadius, canopyRadius));
                    globalIdx++;
                }
            }

            return list;
        }

        private static float HashToRange(int seed, float min, float max)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                float t = (x % 10000) / 10000f;
                return min + (max - min) * t;
            }
        }
    }
}
