using System.Numerics;
using VibeGame.Biomes.Environment;
using VibeGame.Core.WorldObjects;
using VibeGame.Objects;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    // Generic, config-driven spawner for trees as world objects.
    // Uses TreesRegistry and biome AllowedObjects (if provided) or falls back to SpawnRules.BiomeIds.
    public sealed class ConfigTreeWorldObjectSpawner : IWorldObjectSpawner
    {
        private readonly ITreesRegistry _trees;
        private readonly IEnvironmentSampler _sampler;
        private readonly IReadOnlyList<string>? _allowedIds; // may be null/empty

        public ConfigTreeWorldObjectSpawner(ITreesRegistry trees, IEnvironmentSampler sampler, IReadOnlyList<string>? allowedObjectIds = null)
        {
            _trees = trees;
            _sampler = sampler;
            _allowedIds = allowedObjectIds;
        }

        public List<SpawnedObject> GenerateObjects(string biomeId, ITerrainGenerator terrain, float[,] heights, Vector2 originWorld, int count)
        {
            var results = new List<SpawnedObject>();
            // Track placed physics areas (XZ plane) to prevent overlaps between objects across types
            var placedAreas = new List<(Vector2 pos, float radius)>();

            float tile = terrain.TileSize;
            float chunkWorldSize = (heights.GetLength(0) - 1) * tile;

            float margin = MathF.Max(2f * tile, 3f);
            float minX = originWorld.X + margin;
            float maxX = originWorld.X + chunkWorldSize - margin;
            float minZ = originWorld.Y + margin;
            float maxZ = originWorld.Y + chunkWorldSize - margin;

            // Select candidate object ids
            List<TreeObjectConfig> candidateDefs = new();
            if (_allowedIds != null)
            {
                foreach (var id in _allowedIds)
                {
                    if (_trees.TryGet(id, out var def)) candidateDefs.Add(def);
                }
            }
            else
            {
                foreach (var def in _trees.All)
                {
                    // Filter to those whose SpawnRules include this biomeId
                    if (def.SpawnRules?.BiomeIds != null && def.SpawnRules.BiomeIds.Any(b => string.Equals(b, biomeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        candidateDefs.Add(def);
                    }
                }
            }

            if (candidateDefs.Count == 0) return results;

            // Distribute count among candidates
            int perType = Math.Max(1, count / candidateDefs.Count);
            int seedBase = HashCode.Combine(VibeGame.Core.WorldGlobals.Seed, biomeId.GetHashCode(StringComparison.OrdinalIgnoreCase), (int)originWorld.X, (int)originWorld.Y, heights.GetLength(0));

            foreach (var def in candidateDefs)
            {
                var sr = def.SpawnRules ?? new SpawnRulesConfig();
                float altMin = sr.AltitudeRange != null && sr.AltitudeRange.Length > 0 ? sr.AltitudeRange[0] : 0f;
                float altMax = sr.AltitudeRange != null && sr.AltitudeRange.Length > 1 ? sr.AltitudeRange[1] : 1f;
                float tMin = sr.TemperatureRange != null && sr.TemperatureRange.Length > 0 ? sr.TemperatureRange[0] : 0f;
                float tMax = sr.TemperatureRange != null && sr.TemperatureRange.Length > 1 ? sr.TemperatureRange[1] : 1f;
                float mMin = sr.MoistureRange != null && sr.MoistureRange.Length > 0 ? sr.MoistureRange[0] : 0f;
                float mMax = sr.MoistureRange != null && sr.MoistureRange.Length > 1 ? sr.MoistureRange[1] : 1f;

                // Precompute weighted model list
                var models = def.Assets?.Models ?? new List<ModelAsset>();
                if (models.Count == 0) continue; // nothing to draw
                float totalW = 0f; foreach (var m in models) totalW += MathF.Max(0.0001f, m.Weight);

                for (int i = 0; i < perType; i++)
                {
                    int seed = HashCode.Combine(seedBase, def.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), i);
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

                    // Choose model by weighted random (deterministic)
                    float t = ((uint)seed % 10000) / 10000f; // [0,1)
                    string modelPath = models[0].Path;
                    float accum = 0f;
                    foreach (var m in models)
                    {
                        float w = MathF.Max(0.0001f, m.Weight) / totalW;
                        accum += w;
                        if (t <= accum) { modelPath = m.Path; break; }
                    }

                    // Scale: base scale with variance
                    Vector3 baseScale = new Vector3(1f, 1f, 1f);
                    var vis = def.Visual ?? new VisualConfig();
                    if (vis.BaseScale != null && vis.BaseScale.Length >= 3)
                    {
                        baseScale = new Vector3(vis.BaseScale[0], vis.BaseScale[1], vis.BaseScale[2]);
                    }
                    float variance = MathF.Abs(vis.ScaleVariance);
                    float varT = HashToRange(seed * 419 + 101, -variance, variance);
                    Vector3 scale = baseScale * (1.0f + varT);

                    // Rotation: random Y if enabled
                    Quaternion rot = Quaternion.Identity;
                    if (vis.RandomRotationY)
                    {
                        float angle = HashToRange(seed * 887 + 337, 0f, MathF.PI * 2f);
                        rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
                    }

                    // Respect physics area (for spawn spacing) and separate collider radius for player physics
                    float areaRadius = 0f;
                    try { areaRadius = def.Physics != null ? MathF.Max(0f, def.Physics.AreaRadius) : 0f; } catch { areaRadius = 0f; }
                    bool overlaps = false;
                    var candidateXZ = new Vector2(wx, wz);
                    if (placedAreas.Count > 0)
                    {
                        float rr = areaRadius; // current object's spacing radius
                        foreach (var (pos, r) in placedAreas)
                        {
                            float dx = candidateXZ.X - pos.X;
                            float dz = candidateXZ.Y - pos.Y;
                            float minDist = r + rr;
                            if (minDist <= 0f) continue;
                            if ((dx * dx + dz * dz) < (minDist * minDist)) { overlaps = true; break; }
                        }
                    }
                    if (overlaps) continue;

                    float colliderRadius = 0f;
                    try
                    {
                        if (def.Physics != null)
                        {
                            colliderRadius = def.Physics.ColliderRadius > 0f ? def.Physics.ColliderRadius : areaRadius;
                        }
                    }
                    catch { colliderRadius = areaRadius; }

                    results.Add(new SpawnedObject
                    {
                        ObjectId = def.Id,
                        ModelPath = TreesRegistryPathNormalize(modelPath),
                        Position = new Vector3(wx, baseY, wz),
                        Rotation = rot,
                        Scale = scale,
                        CollisionRadius = colliderRadius
                    });

                    // Record placed physics area for subsequent objects
                    placedAreas.Add((candidateXZ, areaRadius));
                }
            }

            return results;
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

        private static string TreesRegistryPathNormalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(AppContext.BaseDirectory, "assets", path.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}