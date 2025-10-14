using System.Numerics;
using VibeGame.Biomes.Environment;
using VibeGame.Core.WorldObjects;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    public sealed class AllowedObjectsTreeSpawner : ITreeSpawner
    {
        private readonly ITreesRegistry _trees;
        private readonly IEnvironmentSampler _sampler;
        private readonly IReadOnlyList<string> _allowedIds;

        public AllowedObjectsTreeSpawner(IReadOnlyList<string> allowedObjectIds, ITreesRegistry trees, IEnvironmentSampler sampler)
        {
            _allowedIds = allowedObjectIds ?? [];
            _trees = trees;
            _sampler = sampler;
        }

        /// <summary>
        /// Primary tree spawning method
        /// </summary>
        public List<SpawnedObject> SpawnTrees(Vector2 origin, ITerrainGenerator terrain, float[,] heights, int count)
        {
            var result = new List<SpawnedObject>();
            if (_allowedIds.Count == 0) return result;

            int chunkSize = heights.GetLength(0);
            float tile = terrain.TileSize;
            float chunkWorldSize = (chunkSize - 1) * tile;
            float margin = MathF.Max(2f * tile, 3f);

            float minX = origin.X + margin;
            float maxX = origin.X + chunkWorldSize - margin;
            float minZ = origin.Y + margin;
            float maxZ = origin.Y + chunkWorldSize - margin;

            int perType = Math.Max(1, count / _allowedIds.Count);
            int seedBase = HashCode.Combine((int)origin.X, (int)origin.Y, chunkSize, 7919);

            foreach (var id in _allowedIds)
            {
                if (!_trees.TryGet(id, out var def)) continue;
                var sr = def.SpawnRules ?? new SpawnRulesConfig();

                float altMin = sr.AltitudeRange?.Length > 0 ? sr.AltitudeRange[0] : 0f;
                float altMax = sr.AltitudeRange?.Length > 1 ? sr.AltitudeRange[1] : 1f;
                float tMin = sr.TemperatureRange?.Length > 0 ? sr.TemperatureRange[0] : 0f;
                float tMax = sr.TemperatureRange?.Length > 1 ? sr.TemperatureRange[1] : 1f;
                float mMin = sr.MoistureRange?.Length > 0 ? sr.MoistureRange[0] : 0f;
                float mMax = sr.MoistureRange?.Length > 1 ? sr.MoistureRange[1] : 1f;

                for (int i = 0; i < perType; i++)
                {
                    int seed = HashCode.Combine(seedBase, id.GetHashCode(StringComparison.OrdinalIgnoreCase), i);

                    float wx = HashToRange(seed * 97 + 5, minX, maxX);
                    float wz = HashToRange(seed * 211 + 23, minZ, maxZ);

                    float baseY = terrain.ComputeHeight(wx, wz);

                    // Slope check
                    if (IsSlopeTooSteep(terrain, wx, wz, baseY)) continue;

                    // Environment filter
                    var env = _sampler.Sample(new Vector2(wx, wz), terrain);
                    if (!IsEnvValid(env, altMin, altMax, tMin, tMax, mMin, mMax)) continue;

                    // Parametric size
                    float trunkHeight = 2.4f + HashToRange(seed * 17 + 11, 0.8f, 3.6f);
                    float trunkRadius = 0.20f + HashToRange(seed * 37 + 19, -0.03f, 0.14f);
                    float canopyRadius = trunkHeight * HashToRange(seed * 41 + 29, 0.45f, 0.72f);

                    result.Add(new SpawnedObject
                    {
                        ObjectId = id,
                        Position = new Vector3(wx, baseY, wz),
                        Scale = new Vector3(trunkRadius, trunkHeight, trunkRadius),
                        Rotation = Quaternion.Identity,
                        CollisionRadius = canopyRadius
                    });
                }
            }

            return result;
        }

        #region Helpers
        private static bool IsSlopeTooSteep(ITerrainGenerator terrain, float x, float z, float baseY)
        {
            float s = 1.5f;
            float ny1 = terrain.ComputeHeight(x + s, z);
            float ny2 = terrain.ComputeHeight(x - s, z);
            float ny3 = terrain.ComputeHeight(x, z + s);
            float ny4 = terrain.ComputeHeight(x, z - s);
            float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)),
                                    MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
            return slope > 2.0f;
        }

        private static bool IsEnvValid(EnvironmentSample env, float altMin, float altMax, float tMin, float tMax, float mMin, float mMax)
        {
            return env.Elevation >= altMin && env.Elevation <= altMax &&
                   env.Temperature >= tMin && env.Temperature <= tMax &&
                   env.Moisture >= mMin && env.Moisture <= mMax;
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
        #endregion

        /// <summary>
        /// Fully implemented IWorldObjectSpawner method
        /// </summary>
        public List<SpawnedObject> GenerateObjects(string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count)
        {
            // Simply call SpawnTrees for API consistency
            return SpawnTrees(originWorld, terrain, heights, count);
        }
    }
}
