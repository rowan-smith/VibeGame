using System.Numerics;
using Veilborne.Biomes.Environment;
using Veilborne.Interfaces;
using Veilborne.Objects;
using Veilborne.Terrain;
using Serilog;
using Veilborne.WorldObjects;

namespace Veilborne.Biomes.Spawners
{
    /// <summary>
    /// Config-driven spawner for trees as world objects.
    /// Uses WorldObjectRegistry and biome AllowedObjects (if provided) or falls back to SpawnRules.BiomeIds.
    /// Supports per-model rotation.
    /// </summary>
    public sealed class ConfigTreeWorldObjectSpawner : IWorldObjectSpawner
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<ConfigTreeWorldObjectSpawner>();
        private readonly IWorldObjectRegistry _trees;
        private readonly IEnvironmentSampler _sampler;
        private readonly ITerrainGenerator _envTerrain;
        private readonly IWorldConfigService _config;
        private readonly IReadOnlyList<string>? _allowedIds;

        public ConfigTreeWorldObjectSpawner(IWorldObjectRegistry trees, IEnvironmentSampler sampler, ITerrainGenerator envTerrain, IWorldConfigService config, IReadOnlyList<string>? allowedObjectIds = null)
        {
            _trees = trees;
            _sampler = sampler;
            _envTerrain = envTerrain;
            _config = config;
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
            List<WorldObjectConfig> candidateDefs = new();
            if (_allowedIds != null && _allowedIds.Count > 0)
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

            int perType = Math.Max(1, count / Math.Max(1, candidateDefs.Count));
            int seedBase = HashCode.Combine(_config.Seed, biomeId.GetHashCode(StringComparison.OrdinalIgnoreCase),
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

                // Target accepted count per type from density.
                float density = Math.Clamp(sr.SpawnDensity, 0f, 2f);
                int targetAccepted = Math.Max(0, (int)MathF.Round(perType * density));
                targetAccepted = Math.Min(targetAccepted, 3);
                if (targetAccepted == 0) continue;
                // We need extra tries because filters (env/slope/overlap) can reject many candidates.
                int attempts = Math.Min(18, Math.Max(targetAccepted * 4, targetAccepted + 2));
                int rejectedSlope = 0;
                int rejectedEnv = 0;
                int rejectedOverlap = 0;
                int accepted = 0;

                for (int i = 0; i < attempts; i++)
                {
                    if (accepted >= targetAccepted)
                        break;

                    int seed = HashCode.Combine(seedBase, def.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), i);
                    float wx = HashToRange(seed * 97 + 5, minX, maxX);
                    float wz = HashToRange(seed * 211 + 23, minZ, maxZ);
                    float baseY = SampleMeshHeight(heights, originWorld, terrain.TileSize, wx, wz);

                    if (IsSlopeTooSteep(heights, originWorld, terrain.TileSize, wx, wz, baseY))
                    {
                        rejectedSlope++;
                        continue;
                    }

                    var env = _sampler.Sample(new Vector2(wx, wz), _envTerrain);
                    if (!IsEnvValid(env, altMin, altMax, tMin, tMax, mMin, mMax))
                    {
                        rejectedEnv++;
                        continue;
                    }

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
                    baseScale = SanitizeScale(baseScale, def.Category);
                    float variance = MathF.Abs(def.Visual?.ScaleVariance ?? 0f);
                    float varT = HashToRange(seed * 419 + 101, -variance, variance);
                    Vector3 scale = baseScale * (1.0f + varT);
                    scale = SanitizeScale(scale, def.Category);

                    // Rotation: explicit model rotation overrides visual random rotation.
                    Quaternion rot = Quaternion.Identity;

                    if (modelRotation.HasValue)
                    {
                        // Apply explicit Y-rotation in degrees from config.
                        rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, modelRotation.Value * (MathF.PI / 180f));
                    }
                    else if (def.Visual?.RandomRotationY == true)
                    {
                        float yaw = HashToRange(seed * 613 + 37, 0f, MathF.PI * 2f);
                        rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
                    }

                    float areaRadius = MathF.Max(def.Physics?.AreaRadius ?? 0f, 0.6f);
                    float colliderRadius = def.Physics?.ColliderRadius > 0f ? def.Physics.ColliderRadius : areaRadius;
                    // ClusterRadius controls clumping distribution; it should not act as hard exclusion radius.
                    float spacingRadius = MathF.Max(colliderRadius, areaRadius * 0.70f);
                    bool overlaps = placedAreas.Any(pa => Vector2.DistanceSquared(pa.pos, new Vector2(wx, wz)) < (pa.radius + spacingRadius) * (pa.radius + spacingRadius));
                    if (overlaps)
                    {
                        rejectedOverlap++;
                        continue;
                    }

                    results.Add(new SpawnedObject
                    {
                        ObjectId = def.Id,
                        ObjectDisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.Id : def.DisplayName,
                        ModelPath = NormalizePath(modelPath),
                        Position = new Vector3(wx, baseY, wz),
                        Rotation = rot,
                        Scale = scale,
                        CollisionRadius = colliderRadius
                    });
                    accepted++;

                    placedAreas.Add((new Vector2(wx, wz), spacingRadius));
                }
                if (accepted == 0)
                {
                    Log.Debug(
                        "Tree spawn produced zero objects for id={ObjectId} biome={BiomeId} chunk=({ChunkX},{ChunkZ}); target={TargetAccepted}, slopeRejected={SlopeRejected}, envRejected={EnvRejected}, overlapRejected={OverlapRejected}; ranges alt=[{AltMin:0.##},{AltMax:0.##}] temp=[{TempMin:0.##},{TempMax:0.##}] moist=[{MoistMin:0.##},{MoistMax:0.##}]",
                        def.Id, biomeId, (int)(originWorld.X / chunkWorldSize), (int)(originWorld.Y / chunkWorldSize),
                        targetAccepted, rejectedSlope, rejectedEnv, rejectedOverlap,
                        altMin, altMax, tMin, tMax, mMin, mMax);
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

        private static bool IsSlopeTooSteep(float[,] heights, Vector2 originWorld, float tile, float x, float z, float baseY)
        {
            float s = MathF.Max(tile, 1.0f);
            float ny1 = SampleMeshHeight(heights, originWorld, tile, x + s, z);
            float ny2 = SampleMeshHeight(heights, originWorld, tile, x - s, z);
            float ny3 = SampleMeshHeight(heights, originWorld, tile, x, z + s);
            float ny4 = SampleMeshHeight(heights, originWorld, tile, x, z - s);
            float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)),
                                    MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
            return slope > 3.5f;
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

        private static Vector3 SanitizeScale(Vector3 scale, string? category)
        {
            bool isTree = !string.IsNullOrWhiteSpace(category) &&
                          category.Contains("tree", StringComparison.OrdinalIgnoreCase);
            // Keep trees from becoming unintentionally tiny due legacy config scales.
            float min = isTree ? 0.35f : 0.01f;
            float x = Math.Clamp(scale.X, min, 4.0f);
            float y = Math.Clamp(scale.Y, min, 4.0f);
            float z = Math.Clamp(scale.Z, min, 4.0f);
            return new Vector3(x, y, z);
        }
        #endregion
    }
}
