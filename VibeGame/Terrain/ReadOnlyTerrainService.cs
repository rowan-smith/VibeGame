using System.Collections.Concurrent;
using System.Numerics;
using ZeroElectric.Vinculum;
using Veilborne.Core.GameWorlds.Terrain;
using VibeGame.Biomes;
using VibeGame.Objects;

namespace VibeGame.Terrain
{
    public class ReadOnlyTerrainService : ITerrainColliderProvider
    {
        public float TileSize { get; } = 2.0f;
        public int ChunkSize { get; } = 64;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly EditableTerrainService _editable;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldObjectRenderer _worldObjectRenderer;

        // Track loaded chunks and preserve their mesh generation state across frames
        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<SpawnedObject>> _objectsByChunk = new();

        // Async generation state
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<((int cx, int cz) key, float[,] heights, Vector2 origin, List<SpawnedObject> objects)> _completed = new();

        public ReadOnlyTerrainService(EditableTerrainService editable, IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer)
        {
            _editable = editable;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _worldObjectRenderer = worldObjectRenderer;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            // Build a set of desired keys within radius
            var desired = new HashSet<(int cx, int cz)>();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                var key = (centerX + x, centerZ + z);
                desired.Add(key);
                if (!_loadedChunks.ContainsKey(key) && !_generating.Contains(key))
                {
                    var origin = new Vector2(key.Item1 * ChunkSize * TileSize, key.Item2 * ChunkSize * TileSize);

                    // Off-thread height sampling from editable ring
                    _generating.Add(key);
                    _ = Task.Run(() =>
                    {
                        // Build heights from editable surface so RO ring reflects nearby edits
                        float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                        for (int zz = 0; zz <= ChunkSize; zz++)
                        for (int xx = 0; xx <= ChunkSize; xx++)
                        {
                            float wx = origin.X + xx * TileSize;
                            float wz = origin.Y + zz * TileSize;
                            heights[xx, zz] = _terrainGen.ComputeHeight(wx, wz);
                        }

                        // Spawn world objects for this chunk using biome spawner
                        var biome = _biomeProvider.GetBiomeAt(origin, _terrainGen);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrainGen, heights, origin, 18);
                        var filtered = new List<SpawnedObject>(raw.Count);
                        foreach (var obj in raw)
                        {
                            var at = _biomeProvider.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _terrainGen);
                            if (string.Equals(at.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                                filtered.Add(obj);
                        }

                        _completed.Enqueue((key, heights, origin, filtered));
                    });
                }
            }

            // Remove chunks that are no longer within the desired radius
            var toRemove = new List<(int cx, int cz)>();
            foreach (var key in _loadedChunks.Keys)
                if (!desired.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove)
            {
                _loadedChunks.Remove(key);
                _objectsByChunk.Remove(key);
            }
        }

        public async Task PumpAsyncJobs()
        {
            while (_completed.TryDequeue(out var item))
            {
                _generating.Remove(item.key);
                // Install generated heightmap; mesh build remains coordinated by TerrainManager
                _loadedChunks[item.key] = new TerrainChunk
                {
                    Heights = item.heights,
                    Origin = item.origin,
                    IsMeshGenerated = false,
                    BuiltFromVersion = -1
                };
                // Store objects for this chunk
                _objectsByChunk[item.key] = item.objects ?? new List<SpawnedObject>();
                await Task.Yield();
            }
        }

        public void RenderTiles(Camera3D camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            foreach (var kvp in _loadedChunks)
            {
                var key = kvp.Key;
                var chunk = kvp.Value;

                if (exclude != null && exclude.Contains(key))
                    continue;

                // Apply biome texture based on chunk center
                var biome = _biomeProvider.GetBiomeAt(
                    new Vector2(chunk.Origin.X + ChunkSize * 0.5f, chunk.Origin.Y + ChunkSize * 0.5f),
                    null
                );
                _renderer.ApplyBiomeTextures(biome.Data);

                // Render the chunk (meshes are built centrally by TerrainManager.UpdateAround)
                _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera);

                // Draw any spawned world objects (e.g., trees) for this chunk
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

        public void Render(Camera3D camera) => RenderTiles(camera);

        public void RenderWithExclusions(Camera3D camera, HashSet<(int cx, int cz)> exclusions)
            => RenderTiles(camera, exclusions);

        public float SampleHeight(float worldX, float worldZ)
            => _editable.SampleHeight(worldX, worldZ);

        public IBiome GetBiomeAt(float worldX, float worldZ)
            => _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), null);

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            foreach (var (cx, cz) in _loadedChunks.Keys)
            {
                Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.2f, ChunkSize * TileSize, Raylib.DARKGREEN);
            }
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            yield return (worldPos + new Vector2(10, 0), 5);
            yield return (worldPos + new Vector2(-8, 7), 3);
        }
    }
}
