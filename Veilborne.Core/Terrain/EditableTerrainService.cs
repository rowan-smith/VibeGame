using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Veilborne.Core.Ecs;
using Veilborne.Core;
using Veilborne.Interfaces;
using Veilborne.Objects;
using Veilborne.Biomes;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Terrain
{
    public class EditableTerrainService
    {
        public bool RenderBaseHeightmap { get; set; } = true;

        // Local adapter so spawners sample the exact same height field as the visible editable mesh
        private sealed class EditableTerrainAdapter : ITerrainGenerator
        {
            private readonly EditableTerrainService _owner;
            public EditableTerrainAdapter(EditableTerrainService owner) { _owner = owner; }
            public int TerrainSize => _owner.ChunkSize;
            public float TileSize => _owner.TileSize;
            public float[,] GenerateHeights()
            {
                int size = TerrainSize;
                float[,] h = new float[size, size];
                for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                {
                    float wx = x * TileSize;
                    float wz = z * TileSize;
                    h[x, z] = _owner.SampleHeight(wx, wz);
                }
                return h;
            }
            public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
            {
                float[,] h = new float[chunkSize + 1, chunkSize + 1];
                float originX = chunkX * chunkSize * TileSize;
                float originZ = chunkZ * chunkSize * TileSize;
                for (int z = 0; z <= chunkSize; z++)
                for (int x = 0; x <= chunkSize; x++)
                {
                    float wx = originX + x * TileSize;
                    float wz = originZ + z * TileSize;
                    h[x, z] = _owner.SampleHeight(wx, wz);
                }
                return h;
            }
            public float SampleHeight(float[,] heights, float worldX, float worldZ)
            {
                int size = heights.GetLength(0);
                float gx = worldX / TileSize;
                float gz = worldZ / TileSize;
                int x0 = Math.Clamp((int)MathF.Floor(gx), 0, size - 1);
                int z0 = Math.Clamp((int)MathF.Floor(gz), 0, size - 1);
                return heights[x0, z0];
            }
            public float ComputeHeight(float worldX, float worldZ) => _owner.SampleHeight(worldX, worldZ);
        }
        public float TileSize { get; } = 1.0f;
        public int ChunkSize { get; } = 32;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldObjectRenderer _worldObjectRenderer;
        private readonly EntityRegistry _entityRegistry;
        private readonly IWorldConfigService _config;

        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<Entity>> _entitiesByChunk = new();
        private readonly Dictionary<(int cx, int cz), BiomeData> _primaryBiomeByChunk = new();
        private readonly Dictionary<(int cx, int cz), (BiomeData? secondary, float blend)> _biomeBlendByChunk = new();
        private readonly object _lock = new();
        private int _lastDesiredChunkCount;

        public EditableTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer, EntityRegistry entityRegistry, IWorldConfigService config)
        {
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _worldObjectRenderer = worldObjectRenderer;
            _entityRegistry = entityRegistry;
            _config = config;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;
        public int DesiredChunkCount => _lastDesiredChunkCount;
        public int LoadedChunkCount => _loadedChunks.Count;

        private (int cx, int cz) WorldToChunkKey(float worldX, float worldZ)
        {
            int cx = (int)MathF.Floor(worldX / (ChunkSize * TileSize));
            int cz = (int)MathF.Floor(worldZ / (ChunkSize * TileSize));
            return (cx, cz);
        }

        private bool TryGetLocalIndex((int cx, int cz) key, float worldX, float worldZ, out int ix, out int iz)
        {
            ix = iz = 0;
            if (!_loadedChunks.TryGetValue(key, out var chunk)) return false;
            float localX = (worldX - chunk.Origin.X) / TileSize;
            float localZ = (worldZ - chunk.Origin.Y) / TileSize;
            ix = Math.Clamp((int)MathF.Round(localX), 0, ChunkSize);
            iz = Math.Clamp((int)MathF.Round(localZ), 0, ChunkSize);
            return true;
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            lock (_lock)
            {
                var desired = new HashSet<(int cx, int cz)>();
                var nearby = TerrainChunkSpatialHash.GetChunksAround(centerX, centerZ, radiusChunks);
                for (int i = 0; i < nearby.Length; i++)
                {
                    var key = nearby[i];
                    desired.Add(key);
                    if (!_loadedChunks.ContainsKey(key))
                    {
                        float originX = key.Item1 * ChunkSize * TileSize;
                        float originZ = key.Item2 * ChunkSize * TileSize;
                        // Create a heightmap that includes the outer edge so adjacent chunks share borders
                        float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                        for (int zz = 0; zz <= ChunkSize; zz++)
                        for (int xx = 0; xx <= ChunkSize; xx++)
                        {
                            float wx = originX + xx * TileSize;
                            float wz = originZ + zz * TileSize;
                            heights[xx, zz] = SampleHeight(wx, wz);
                        }

                        _loadedChunks[key] = new TerrainChunk
                        {
                            Heights = heights,
                            BaseHeights = (float[,])heights.Clone(),
                            Origin = new Vector2(originX, originZ),
                            IsMeshGenerated = false,
                            Dirty = true,
                            Version = 0,
                            BuiltFromVersion = -1
                        };

                        var center = new Vector2(
                            originX + ChunkSize * TileSize * 0.5f,
                            originZ + ChunkSize * TileSize * 0.5f);
                        var (primaryBiome, secondaryBiome, secondaryBlend) = ResolveBiomeBlend(center);
                        _primaryBiomeByChunk[key] = primaryBiome;
                        _biomeBlendByChunk[key] = (secondaryBiome, secondaryBlend);
                        var newChunk = _loadedChunks[key];
                        InitializeChunkLayersAndResources(key, ref newChunk, primaryBiome.Id);
                        _loadedChunks[key] = newChunk;

                        // Spawn world objects for this editable chunk
                        var origin = new Vector2(originX, originZ);
                        var biome = _biomeProvider.GetBiomeAt(origin, _terrainGen);
                        var adapter = new EditableTerrainAdapter(this);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, adapter, heights, origin, 18);
                        var entities = new List<Entity>();
                        foreach (var obj in raw)
                        {
                            var entity = _entityRegistry.CreateEntity();
                            entity.AddComponent(new TransformComponent
                            {
                                Position = obj.Position,
                                Rotation = obj.Rotation,
                                Scale = obj.Scale
                            });
                            entity.AddComponent(new RenderComponent
                            {
                                ModelPath = obj.ModelPath,
                                ConfigRotationDegrees = obj.ConfigRotationDegrees
                            });
                            entity.AddComponent(new WorldObjectComponent());
                            entity.AddComponent(new TagComponent { Name = "WorldObject" });
                            entity.AddComponent(new TeamComponent { Id = 0 });
                            entity.AddComponent(new NameComponent { Value = obj.ModelPath });
                            entity.AddComponent(new ParentComponent { EntityId = -1 });
                            entity.AddComponent(new DirtyComponent { NeedsUpdate = false });
                            entity.AddComponent(new LifetimeComponent { RemainingSeconds = 0f });
                            entity.AddComponent(new BillboardComponent { FaceCamera = false });
                            entity.AddComponent(new ShadowCasterComponent { CastsShadows = true });
                            entity.AddComponent(new MaterialComponent { ShaderId = string.Empty, Tint = Vector4.One });
                            entity.AddComponent(new ColliderComponent
                            {
                                Radius = WorldObjectCollisionRules.ComputeColliderRadius(obj)
                            });
                            entity.AddComponent(WorldObjectCollisionRules.GetFilter(obj));
                            entity.AddComponent(new RigidbodyComponent
                            {
                                IsKinematic = true,
                                IsSleeping = false
                            });
                            entity.AddComponent(new TerrainChunkComponent
                            {
                                ChunkX = key.Item1,
                                ChunkZ = key.Item2,
                                LodLevel = 0
                            });
                            entity.AddComponent(new LodComponent
                            {
                                Level = 0
                            });
                            entity.AddComponent(new BiomeComponent
                            {
                                BiomeId = biome.Id
                            });
                            entities.Add(entity);
                        }
                        _entitiesByChunk[key] = entities;
                    }
                }

                // unload chunks no longer desired
                var toRemove = new List<(int cx, int cz)>();
                foreach (var key in _loadedChunks.Keys)
                    if (!desired.Contains(key)) toRemove.Add(key);
                foreach (var key in toRemove)
                {
                    _loadedChunks.Remove(key);
                    _primaryBiomeByChunk.Remove(key);
                    _biomeBlendByChunk.Remove(key);
                    if (_entitiesByChunk.TryGetValue(key, out var entities))
                    {
                        foreach (var entity in entities)
                            _entityRegistry.DestroyEntity(entity);
                        _entitiesByChunk.Remove(key);
                    }
                }

                _lastDesiredChunkCount = desired.Count;
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // If this position is in a loaded editable chunk, bilinearly sample from its heightmap (includes edits)
            var key = WorldToChunkKey(worldX, worldZ);
            lock (_lock)
            {
                if (_loadedChunks.TryGetValue(key, out var chunk))
                {
                    return SampleFromChunk(chunk, worldX, worldZ);
                }
            }

            // Fallback to procedural world height when this editable chunk isn't loaded.
            // Blend biome influences in world-space to keep transitions organic and decoupled from chunk bounds.
            float baseHeight = _terrainGen.ComputeHeight(worldX, worldZ);
            return BiomeTerrainHeightBlender.ComputeHeight(worldX, worldZ, baseHeight, _biomeProvider, _terrainGen);
        }

        private float SampleFromChunk(TerrainChunk chunk, float worldX, float worldZ)
        {
            // Bilinear interpolation of the chunk heightmap at arbitrary world coordinates
            float localX = (worldX - chunk.Origin.X) / TileSize;
            float localZ = (worldZ - chunk.Origin.Y) / TileSize;

            int x0 = Math.Clamp((int)MathF.Floor(localX), 0, ChunkSize);
            int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, ChunkSize);
            int x1 = Math.Clamp(x0 + 1, 0, ChunkSize);
            int z1 = Math.Clamp(z0 + 1, 0, ChunkSize);

            float tx = Math.Clamp(localX - x0, 0f, 1f);
            float tz = Math.Clamp(localZ - z0, 0f, 1f);

            float h00 = chunk.Heights[x0, z0];
            float h10 = chunk.Heights[x1, z0];
            float h01 = chunk.Heights[x0, z1];
            float h11 = chunk.Heights[x1, z1];

            float hx0 = Lerp(h00, h10, tx);
            float hx1 = Lerp(h01, h11, tx);
            return Lerp(hx0, hx1, tz);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private (BiomeData primary, BiomeData? secondary, float blend) ResolveBiomeBlend(Vector2 centerWorld)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
            {
                var (primary, secondary, blend) = simple.GetBiomeBlendAt(centerWorld, _terrainGen);
                return (primary.Data, secondary?.Data, blend);
            }

            var primaryBiome = _biomeProvider.GetBiomeAt(centerWorld, _terrainGen);
            return (primaryBiome.Data, null, 0f);
        }

        private void RecomputeSplatmapForChunk((int cx, int cz) key, ref TerrainChunk chunk)
        {
            int w = chunk.Heights.GetLength(0);
            int h = chunk.Heights.GetLength(1);
            chunk.Splatmap ??= new Vector4[w, h];

            string biomeId = _primaryBiomeByChunk.TryGetValue(key, out var biome) ? biome.Id : string.Empty;
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (chunk.BaseHeights != null)
                    depth = MathF.Max(0f, chunk.BaseHeights[x, z] - chunk.Heights[x, z]);
                chunk.Splatmap[x, z] = ComputeSplatWeights(depth, key, x, z, biomeId);
            }
        }

        public void Render(CameraComponent camera)
        {
            if (!RenderBaseHeightmap) return;

            lock (_lock)
            {
                foreach (var kvp in _loadedChunks)
                {
                    var key = kvp.Key;
                    var chunk = kvp.Value;
                    if (!_primaryBiomeByChunk.TryGetValue(key, out var primaryBiome))
                    {
                        var center = new Vector2(
                            chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                            chunk.Origin.Y + ChunkSize * TileSize * 0.5f);
                        var (primary, secondary, blend) = ResolveBiomeBlend(center);
                        primaryBiome = primary;
                        _primaryBiomeByChunk[key] = primaryBiome;
                        _biomeBlendByChunk[key] = (secondary, blend);
                    }
                    if (!_biomeBlendByChunk.TryGetValue(key, out var blendInfo))
                        blendInfo = (null, 0f);
                _renderer.ApplyBiomeBlendTextures(primaryBiome, blendInfo.secondary, blendInfo.blend);
                    _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera, chunk.BaseHeights, _config.Config.TerrainLayers, chunk.Splatmap);
                }
            }
        }

        public void RenderDebugChunkBounds(CameraComponent camera)
        {
            // Debug chunk bounds are drawn by VeilborneEngine as projected 2D overlays.
        }

        public IEnumerable<(Vector3 center, Vector3 size)> EnumerateChunkBounds()
        {
            lock (_lock)
            {
                foreach (var chunk in _loadedChunks.Values)
                {
                    float worldSize = ChunkSize * TileSize;
                    yield return (
                        new Vector3(chunk.Origin.X + worldSize * 0.5f, 0f, chunk.Origin.Y + worldSize * 0.5f),
                        new Vector3(worldSize, 2f, worldSize));
                }
            }
        }

        private static float EvalFalloff(float t, VoxelFalloff falloff)
        {
            t = Math.Clamp(t, 0f, 1f);
            return falloff switch
            {
                VoxelFalloff.Linear => 1f - t,
                VoxelFalloff.Exponential => (1f - t) * (1f - t),
                VoxelFalloff.Cosine => 0.5f * (1f + MathF.Cos(MathF.PI * t)),
                _ => 1f - t
            };
        }

        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            lock (_lock)
            {
                float maxDepth = MathF.Max(0.2f, _config.Config.DigMaxDepth);
                float smoothness = Math.Clamp(_config.Config.DigSmoothness, 0f, 0.6f);
                float minX = worldCenter.X - radius;
                float maxX = worldCenter.X + radius;
                float minZ = worldCenter.Z - radius;
                float maxZ = worldCenter.Z + radius;

                int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
                int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
                int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
                int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));

                for (int cz = minCz; cz <= maxCz; cz++)
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    var key = (cx, cz);
                    if (!_loadedChunks.TryGetValue(key, out var chunk)) continue;

                    float originX = chunk.Origin.X;
                    float originZ = chunk.Origin.Y;

                    int x0 = Math.Clamp((int)MathF.Floor((minX - originX) / TileSize), 0, ChunkSize);
                    int x1 = Math.Clamp((int)MathF.Ceiling((maxX - originX) / TileSize), 0, ChunkSize);
                    int z0 = Math.Clamp((int)MathF.Floor((minZ - originZ) / TileSize), 0, ChunkSize);
                    int z1 = Math.Clamp((int)MathF.Ceiling((maxZ - originZ) / TileSize), 0, ChunkSize);

                    for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float wx = originX + ix * TileSize;
                        float wz = originZ + iz * TileSize;
                        float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(worldCenter.X, worldCenter.Z));
                        if (d > radius) continue;
                        float t = d / radius;
                        float delta = strength * EvalFalloff(t, falloff) * 0.16f;
                        float floor = chunk.BaseHeights != null ? chunk.BaseHeights[ix, iz] - maxDepth : float.NegativeInfinity;
                        float next = chunk.Heights[ix, iz] - delta;
                        chunk.Heights[ix, iz] = MathF.Max(floor, next); // dig lowers terrain with finite depth cap
                    }

                    if (smoothness > 0.001f && falloff != VoxelFalloff.Exponential)
                        SmoothPatch(chunk, x0, z0, x1, z1, maxDepth, smoothness);
                    RecomputeSplatmapForChunk(key, ref chunk);

                    // Mark dirty subregion (+1 padding for normal continuity)
                    int pd = 1;
                    chunk.MarkDirtyRect(
                        Math.Clamp(x0 - pd, 0, ChunkSize),
                        Math.Clamp(z0 - pd, 0, ChunkSize),
                        Math.Clamp(x1 + pd, 0, ChunkSize),
                        Math.Clamp(z1 + pd, 0, ChunkSize));

                    chunk.IsMeshGenerated = false;
                    chunk.Dirty = true;
                    chunk.Version++;
                    _loadedChunks[key] = chunk;
                }
            }
            return Task.CompletedTask;
        }

        public bool TryMineAt(Vector3 position, float power, out ResourceBlockType blockType)
        {
            blockType = ResourceBlockType.None;
            lock (_lock)
            {
                var key = WorldToChunkKey(position.X, position.Z);
                if (!_loadedChunks.TryGetValue(key, out var chunk))
                    return false;
                if (!TryGetLocalIndex(key, position.X, position.Z, out int ix, out int iz))
                    return false;
                if (chunk.BaseHeights == null)
                    return false;

                float depth = MathF.Max(0f, chunk.BaseHeights[ix, iz] - chunk.Heights[ix, iz]);
                var lc = _config.Config.TerrainLayers;
                var voxelKey = (ix, iz);
                if (!chunk.ResourceVoxels.TryGetValue(voxelKey, out var voxel))
                {
                    voxel = CreateResourceVoxelForCell(key, ix, iz, depth);
                    chunk.ResourceVoxels[voxelKey] = voxel;
                }

                if (power <= 0f)
                {
                    blockType = voxel.Type;
                    return voxel.Type != ResourceBlockType.None;
                }

                voxel.Density = Math.Clamp(voxel.Density - power, 0f, 1f);
                if (voxel.Density <= 0f)
                {
                    blockType = voxel.Type;
                    chunk.ResourceVoxels.Remove(voxelKey);
                    if (voxel.Type != ResourceBlockType.None && depth >= lc.SubsurfaceDepth)
                    {
                        float carve = MathF.Max(0.02f, power * 0.25f);
                        float floor = chunk.BaseHeights[ix, iz] - MathF.Max(0.2f, _config.Config.DigMaxDepth);
                        chunk.Heights[ix, iz] = MathF.Max(floor, chunk.Heights[ix, iz] - carve);
                        chunk.MarkDirtyCell(ix, iz);
                        chunk.Dirty = true;
                        chunk.Version++;
                    }
                    RecomputeSplatmapForChunk(key, ref chunk);
                    _loadedChunks[key] = chunk;
                    return true;
                }

                chunk.ResourceVoxels[voxelKey] = voxel;
                _loadedChunks[key] = chunk;
                return false;
            }
        }

        private void InitializeChunkLayersAndResources((int cx, int cz) key, ref TerrainChunk chunk, string biomeId)
        {
            int w = chunk.Heights.GetLength(0);
            int h = chunk.Heights.GetLength(1);
            chunk.Splatmap = new Vector4[w, h];
            chunk.ResourceVoxels.Clear();

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (chunk.BaseHeights != null)
                    depth = MathF.Max(0f, chunk.BaseHeights[x, z] - chunk.Heights[x, z]);
                chunk.Splatmap[x, z] = ComputeSplatWeights(depth, key, x, z, biomeId);
            }
        }

        private Vector4 ComputeSplatWeights(float depth, (int cx, int cz) key, int ix, int iz, string biomeId)
        {
            var lc = _config.Config.TerrainLayers;
            float grass = Math.Clamp(1f - depth / MathF.Max(0.05f, lc.SubsurfaceDepth), 0f, 1f);
            float mud = 0f;
            float rock = 0f;
            float mineral = 0f;

            if (depth > 0f)
            {
                float subT = Math.Clamp(depth / MathF.Max(0.05f, lc.SubsurfaceDepth), 0f, 1f);
                float deepT = Math.Clamp((depth - lc.SubsurfaceDepth) / MathF.Max(0.05f, lc.DeepDepth - lc.SubsurfaceDepth), 0f, 1f);
                mud = Math.Clamp(subT * (1f - deepT), 0f, 1f);
                rock = deepT;
                mineral = ComputeMineralWeight(key, ix, iz, depth, biomeId);
                rock = Math.Clamp(rock * (1f - mineral), 0f, 1f);
            }

            float sum = grass + mud + rock + mineral;
            if (sum <= 1e-5f) return new Vector4(1f, 0f, 0f, 0f);
            return new Vector4(grass / sum, mud / sum, rock / sum, mineral / sum);
        }

        private float ComputeMineralWeight((int cx, int cz) key, int ix, int iz, float depth, string biomeId)
        {
            if (!_config.Config.BiomeMining.TryGetValue(biomeId, out var rule))
                return 0f;
            if (depth < rule.OreMinDepth || depth > rule.OreMaxDepth)
                return 0f;

            int h = HashCode.Combine(_config.Seed, key.cx, key.cz, ix, iz, biomeId.GetHashCode(StringComparison.OrdinalIgnoreCase));
            float n = HashTo01(h * 1109 + (int)(rule.OreNoiseFrequency * 100f));
            if (n < rule.OreThreshold)
                return 0f;
            return Math.Clamp((n - rule.OreThreshold) / MathF.Max(0.01f, 1f - rule.OreThreshold), 0f, 1f) * Math.Clamp(rule.OreSpawnChance * 3f, 0f, 1f);
        }

        private ResourceVoxel CreateResourceVoxelForCell((int cx, int cz) key, int ix, int iz, float depth)
        {
            var lc = _config.Config.TerrainLayers;
            ResourceBlockType type = ResourceBlockType.Grass;
            if (depth >= lc.DeepDepth) type = ResourceBlockType.Rock;
            else if (depth >= lc.SubsurfaceDepth) type = ResourceBlockType.Dirt;

            string biomeId = _primaryBiomeByChunk.TryGetValue(key, out var biome) ? biome.Id : string.Empty;
            if (!string.IsNullOrEmpty(biomeId) && _config.Config.BiomeMining.TryGetValue(biomeId, out var rule))
            {
                if (depth >= rule.OreMinDepth && depth <= rule.OreMaxDepth)
                {
                    float n = HashTo01(HashCode.Combine(_config.Seed, key.cx, key.cz, ix, iz, 79));
                    if (n >= rule.OreThreshold)
                        type = ParseOreType(rule.OreType);
                }
            }

            return new ResourceVoxel
            {
                LocalPosition = new Vector3(ix * TileSize, 0f, iz * TileSize),
                Type = type,
                Density = 1f,
                BiomeId = biomeId
            };
        }

        private static float HashTo01(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return (x % 10000) / 10000f;
            }
        }

        private static ResourceBlockType ParseOreType(string oreType)
        {
            if (string.IsNullOrWhiteSpace(oreType)) return ResourceBlockType.Rock;
            return oreType.Trim().ToLowerInvariant() switch
            {
                "coal" => ResourceBlockType.Coal,
                "iron" => ResourceBlockType.Iron,
                "copper" => ResourceBlockType.Copper,
                _ => ResourceBlockType.Rock
            };
        }

        private static void SmoothPatch(TerrainChunk chunk, int x0, int z0, int x1, int z1, float maxDepth, float smoothness)
        {
            int sx0 = Math.Max(1, x0 - 1);
            int sz0 = Math.Max(1, z0 - 1);
            int sx1 = Math.Min(chunk.Heights.GetLength(0) - 2, x1 + 1);
            int sz1 = Math.Min(chunk.Heights.GetLength(1) - 2, z1 + 1);
            if (sx0 > sx1 || sz0 > sz1) return;

            int w = sx1 - sx0 + 1;
            int h = sz1 - sz0 + 1;
            var temp = new float[w, h];

            for (int z = sz0; z <= sz1; z++)
            for (int x = sx0; x <= sx1; x++)
            {
                float sum = 0f;
                int cnt = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    sum += chunk.Heights[nx, nz];
                    cnt++;
                }

                float avg = cnt > 0 ? sum / cnt : chunk.Heights[x, z];
                float blended = chunk.Heights[x, z] + (avg - chunk.Heights[x, z]) * smoothness;
                float floor = chunk.BaseHeights != null ? chunk.BaseHeights[x, z] - maxDepth : float.NegativeInfinity;
                temp[x - sx0, z - sz0] = MathF.Max(floor, blended);
            }

            for (int z = sz0; z <= sz1; z++)
            for (int x = sx0; x <= sx1; x++)
                chunk.Heights[x, z] = temp[x - sx0, z - sz0];
        }

        public Task PlaceSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            lock (_lock)
            {
                float minX = worldCenter.X - radius;
                float maxX = worldCenter.X + radius;
                float minZ = worldCenter.Z - radius;
                float maxZ = worldCenter.Z + radius;

                int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
                int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
                int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
                int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));

                for (int cz = minCz; cz <= maxCz; cz++)
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    var key = (cx, cz);
                    if (!_loadedChunks.TryGetValue(key, out var chunk)) continue;

                    float originX = chunk.Origin.X;
                    float originZ = chunk.Origin.Y;

                    int x0 = Math.Clamp((int)MathF.Floor((minX - originX) / TileSize), 0, ChunkSize);
                    int x1 = Math.Clamp((int)MathF.Ceiling((maxX - originX) / TileSize), 0, ChunkSize);
                    int z0 = Math.Clamp((int)MathF.Floor((minZ - originZ) / TileSize), 0, ChunkSize);
                    int z1 = Math.Clamp((int)MathF.Ceiling((maxZ - originZ) / TileSize), 0, ChunkSize);

                    for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float wx = originX + ix * TileSize;
                        float wz = originZ + iz * TileSize;
                        float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(worldCenter.X, worldCenter.Z));
                        if (d > radius) continue;
                        float t = d / radius;
                        float delta = strength * EvalFalloff(t, falloff);
                        chunk.Heights[ix, iz] += delta; // place raises terrain
                    }

                    RecomputeSplatmapForChunk(key, ref chunk);

                    // Mark dirty subregion (+1 padding for normal continuity)
                    int pd = 1;
                    chunk.MarkDirtyRect(
                        Math.Clamp(x0 - pd, 0, ChunkSize),
                        Math.Clamp(z0 - pd, 0, ChunkSize),
                        Math.Clamp(x1 + pd, 0, ChunkSize),
                        Math.Clamp(z1 + pd, 0, ChunkSize));

                    chunk.IsMeshGenerated = false;
                    chunk.Dirty = true;
                    chunk.Version++;
                    _loadedChunks[key] = chunk;
                }
            }
            return Task.CompletedTask;
        }

        public int GetMaxVersionForBounds(float minX, float minZ, float maxX, float maxZ)
        {
            lock (_lock)
            {
                int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
                int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
                int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
                int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));
                int maxVer = 0;
                for (int cz = minCz; cz <= maxCz; cz++)
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (_loadedChunks.TryGetValue((cx, cz), out var ch))
                        maxVer = Math.Max(maxVer, ch.Version);
                }
                return maxVer;
            }
        }

        public Task PumpAsyncJobs() => Task.CompletedTask;
    }
}
