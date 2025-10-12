using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ZeroElectric.Vinculum;
using Veilborne.Core.GameWorlds.Terrain;
using VibeGame.Biomes;
using VibeGame.Objects;

namespace VibeGame.Terrain
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

        // Cache loaded chunks with their heightmaps; meshes are built by TerrainManager
        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<SpawnedObject>> _objectsByChunk = new();
        private readonly object _lock = new();

        public EditableTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer)
        {
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _worldObjectRenderer = worldObjectRenderer;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;

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
                for (int z = -radiusChunks; z <= radiusChunks; z++)
                for (int x = -radiusChunks; x <= radiusChunks; x++)
                {
                    var key = (centerX + x, centerZ + z);
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
                            Origin = new Vector2(originX, originZ),
                            IsMeshGenerated = false,
                            Dirty = true,
                            Version = 0,
                            BuiltFromVersion = -1
                        };

                        // Spawn world objects for this editable chunk
                        var origin = new Vector2(originX, originZ);
                        var biome = _biomeProvider.GetBiomeAt(origin, _terrainGen);
                        var adapter = new EditableTerrainAdapter(this);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, adapter, heights, origin, 18);
                        var filtered = new List<SpawnedObject>(raw.Count);
                        foreach (var obj in raw)
                        {
                            var at = _biomeProvider.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _terrainGen);
                            if (string.Equals(at.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                                filtered.Add(obj);
                        }
                        _objectsByChunk[key] = filtered;
                    }
                }

                // unload chunks no longer desired
                var toRemove = new List<(int cx, int cz)>();
                foreach (var key in _loadedChunks.Keys)
                    if (!desired.Contains(key)) toRemove.Add(key);
                foreach (var key in toRemove)
                {
                    _loadedChunks.Remove(key);
                    _objectsByChunk.Remove(key);
                }
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

            // Fallback to base procedural if not loaded
            return MathF.Sin(worldX * 0.1f) * MathF.Cos(worldZ * 0.1f) * 2f;
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

        public void Render(Camera3D camera)
        {
            if (!RenderBaseHeightmap) return;

            lock (_lock)
            {
                foreach (var kvp in _loadedChunks)
                {
                    var key = kvp.Key;
                    var chunk = kvp.Value;

                    // Apply biome textures based on center of chunk
                    var center = new Vector2(chunk.Origin.X + ChunkSize * TileSize * 0.5f, chunk.Origin.Y + ChunkSize * TileSize * 0.5f);
                    var biome = _biomeProvider.GetBiomeAt(center, null);

                    _renderer.ApplyBiomeTextures(biome.Data);
                    _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera);

                    // Draw spawned world objects for this editable chunk
                    if (_objectsByChunk.TryGetValue(key, out var objs) && objs is { Count: > 0 })
                    {
                        // Simple distance culling to avoid drawing distant objects
                        Vector3 camPos = new Vector3(camera.position.X, camera.position.Y, camera.position.Z);
                        const float maxDist = 180f;
                        float maxDist2 = maxDist * maxDist;
                        foreach (var obj in objs)
                        {
                            if (Vector3.DistanceSquared(camPos, obj.Position) > maxDist2) continue;
                            _worldObjectRenderer.DrawWorldObject(obj);
                        }
                    }
                }
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            lock (_lock)
            {
                foreach (var (cx, cz) in _loadedChunks.Keys)
                {
                    Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                    Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.2f, ChunkSize * TileSize, Raylib.RED);
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

        public async Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            await Task.Yield();
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
                        chunk.Heights[ix, iz] -= delta; // dig lowers terrain
                    }

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
                }
            }
        }

        public async Task PlaceSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            await Task.Yield();
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
                }
            }
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
