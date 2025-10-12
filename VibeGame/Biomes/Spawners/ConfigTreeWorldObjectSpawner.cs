using System.Numerics;
using VibeGame.Biomes.Environment;
using VibeGame.Core;
using VibeGame.Core.WorldObjects;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    /// <summary>
    /// Config-driven spawner for trees as world objects.
    /// Uses TreesRegistry and biome AllowedObjects (if provided) or falls back to SpawnRules.BiomeIds.
    /// Supports per-model rotation.
    /// </summary>
    public sealed class ConfigTreeWorldObjectSpawner : IWorldObjectSpawner
    {
        private readonly ITreesRegistry _trees;
        private readonly IEnvironmentSampler _sampler;
        private readonly ITerrainGenerator _envTerrain;
        private readonly IReadOnlyList<string>? _allowedIds;

        public ConfigTreeWorldObjectSpawner(ITreesRegistry trees, IEnvironmentSampler sampler, ITerrainGenerator envTerrain, IReadOnlyList<string>? allowedObjectIds = null)
        {
            _trees = trees;
            _sampler = sampler;
            _envTerrain = envTerrain;
            _allowedIds = allowedObjectIds;
        }

        public List<SpawnedObject> GenerateObjects(string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count)
        {
            var results = new List<SpawnedObject>();
            if (_trees.All.Count == 0) return results;

            int chunkSize = heights.GetLength(0);
            float tile = terrain.TileSize;
            float chunkWorldSize = (chunkSize - 1) * tile;

            float margin = MathF.Max(2f * tile, 3f);
            float minX = originWorld.X + margin;
            float maxX = originWorld.X + chunkWorldSize - margin;
            float minZ = originWorld.Y + margin;
            float maxZ = originWorld.Y + chunkWorldSize - margin;

            // Build candidate tree list
            List<TreeObjectConfig> candidateDefs = new();
            if (_allowedIds != null)
            {
                foreach (var id in _allowedIds)
                    if (_trees.TryGet(id, out var def)) candidateDefs.Add(def);
            }
            else
            {
                foreach (var def in _trees.All)
                {
                    if (def.SpawnRules?.BiomeIds != null && def.SpawnRules.BiomeIds.Any(b => string.Equals(b, biomeId, StringComparison.OrdinalIgnoreCase)))
                        candidateDefs.Add(def);
                }
            }

            if (candidateDefs.Count == 0) return results;

            int perType = Math.Max(1, count / candidateDefs.Count);
            int seedBase = HashCode.Combine(WorldGlobals.Seed, biomeId.GetHashCode(StringComparison.OrdinalIgnoreCase),
                                            (int)originWorld.X, (int)originWorld.Y, chunkSize);

            var placedAreas = new List<(Vector2 pos, float radius)>();

            foreach (var def in candidateDefs)
            {
                var sr = def.SpawnRules ?? new SpawnRulesConfig();
                float altMin = sr.AltitudeRange?.Length > 0 ? sr.AltitudeRange[0] : 0f;
                float altMax = sr.AltitudeRange?.Length > 1 ? sr.AltitudeRange[1] : 1f;
                float tMin = sr.TemperatureRange?.Length > 0 ? sr.TemperatureRange[0] : 0f;
                float tMax = sr.TemperatureRange?.Length > 1 ? sr.TemperatureRange[1] : 1f;
                float mMin = sr.MoistureRange?.Length > 0 ? sr.MoistureRange[0] : 0f;
                float mMax = sr.MoistureRange?.Length > 1 ? sr.MoistureRange[1] : 1f;

                // Weighted models
                var models = def.Assets?.Models ?? new List<ModelAsset>();
                if (models.Count == 0) continue;
                float totalW = models.Sum(m => MathF.Max(0.0001f, m.Weight));

                for (int i = 0; i < perType; i++)
                {
                    int seed = HashCode.Combine(seedBase, def.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), i);
                    float wx = HashToRange(seed * 97 + 5, minX, maxX);
                    float wz = HashToRange(seed * 211 + 23, minZ, maxZ);
                    float baseY = SampleMeshHeight(heights, originWorld, terrain.TileSize, wx, wz);

                    if (IsSlopeTooSteep(terrain, wx, wz, baseY)) continue;

                    var env = _sampler.Sample(new Vector2(wx, wz), _envTerrain);
                    if (!IsEnvValid(env, altMin, altMax, tMin, tMax, mMin, mMax)) continue;

                    // Select weighted model
                    ModelAsset selectedModel = models[0];
                    float tRand = ((uint)seed % 10000) / 10000f;
                    float accum = 0f;
                    foreach (var m in models)
                    {
                        float w = MathF.Max(0.0001f, m.Weight) / totalW;
                        accum += w;
                        if (tRand <= accum)
                        {
                            selectedModel = m;
                            break;
                        }
                    }

                    string modelPath = selectedModel.Path;
                    float? modelRotation = selectedModel.Rotation; // degrees; nullable means: if present, use and skip random

                    // Scale
                    Vector3 baseScale = def.Visual?.BaseScale?.Length >= 3
                        ? new Vector3(def.Visual.BaseScale[0], def.Visual.BaseScale[1], def.Visual.BaseScale[2])
                        : Vector3.One;
                    float variance = MathF.Abs(def.Visual?.ScaleVariance ?? 0f);
                    float varT = HashToRange(seed * 419 + 101, -variance, variance);
                    Vector3 scale = baseScale * (1.0f + varT);

                    // Rotation: use explicit model Rotation if provided; otherwise keep identity (no auto/random rotation)
                    Quaternion rot = Quaternion.Identity;

                    if (modelRotation.HasValue)
                    {
                        // Apply explicit Y-rotation in degrees from config and ignore any random rotation
                        rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, modelRotation.Value * (MathF.PI / 180f));
                    }

                    float areaRadius = def.Physics?.AreaRadius ?? 0f;
                    bool overlaps = placedAreas.Any(pa => Vector2.DistanceSquared(pa.pos, new Vector2(wx, wz)) < (pa.radius + areaRadius) * (pa.radius + areaRadius));
                    if (overlaps) continue;

                    float colliderRadius = def.Physics?.ColliderRadius > 0f ? def.Physics.ColliderRadius : areaRadius;

                    results.Add(new SpawnedObject
                    {
                        ObjectId = def.Id,
                        ModelPath = NormalizePath(modelPath),
                        Position = new Vector3(wx, baseY, wz),
                        Rotation = rot,
                        Scale = scale,
                        CollisionRadius = colliderRadius,
                        ConfigRotationDegrees = modelRotation
                    });

                    placedAreas.Add((new Vector2(wx, wz), areaRadius));
                }
            }

            return results;
        }

        #region Helpers
        private static float SampleMeshHeight(float[,] heights, Vector2 originWorld, float tile, float wx, float wz)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            if (w < 2 || h < 2) return 0f;

            float lx = (wx - originWorld.X) / tile;
            float lz = (wz - originWorld.Y) / tile;

            int x0 = Math.Clamp((int)MathF.Floor(lx), 0, w - 2);
            int z0 = Math.Clamp((int)MathF.Floor(lz), 0, h - 2);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float tx = Math.Clamp(lx - x0, 0f, 1f);
            float tz = Math.Clamp(lz - z0, 0f, 1f);

            float h00 = heights[x0, z0];
            float h10 = heights[x1, z0];
            float h01 = heights[x0, z1];
            float h11 = heights[x1, z1];

            if (tx + tz <= 1f)
                return h00 + (h10 - h00) * tx + (h01 - h00) * tz;
            else
                return h11 + (h10 - h11) * (1f - tz) + (h01 - h11) * (1f - tx);
        }

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

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(AppContext.BaseDirectory, "assets", path.Replace('/', Path.DirectorySeparatorChar));
        }
        #endregion
    }
}
