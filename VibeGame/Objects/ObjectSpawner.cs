using System.Collections.Concurrent;
using System.Numerics;
using VibeGame.Biomes;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    // High-level orchestrator for deterministic world object spawning per chunk.
    public sealed class ObjectSpawner
    {
        private readonly int _seed;
        private readonly ITerrainGenerator _terrain;
        private readonly IBiomeProvider _biomes;
        private readonly ConcurrentDictionary<(int cx, int cz), List<SpawnedObject>> _cache = new();

        public ObjectSpawner(int seed, ITerrainGenerator terrain, IBiomeProvider biomes)
        {
            _seed = seed;
            _terrain = terrain;
            _biomes = biomes;
        }

        public IReadOnlyList<SpawnedObject> GetObjectsForChunk((int cx, int cz) key)
        {
            return _cache.TryGetValue(key, out var list) ? list : Array.Empty<SpawnedObject>();
        }

        public void EnsureObjects(Vector3 playerPos, Dictionary<Vector3, Chunk> activeChunks, VibeGame.Core.AsyncTaskQueue async)
        {
            var (ccx, ccz) = WorldToChunk(playerPos.X, playerPos.Z);
            int radius = 4;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var key = (ccx + dx, ccz + dz);
                if (_cache.ContainsKey(key)) continue;
                async.Enqueue(() => SpawnChunkAsync(key));
            }
        }

        private Task SpawnChunkAsync((int cx, int cz) key)
        {
            // Generate base heights for this chunk
            int chunkSize = _terrain.TerrainSize;
            float tile = _terrain.TileSize;
            float[,] heights = _terrain.GenerateHeightsForChunk(key.cx, key.cz, chunkSize);
            float chunkWorld = (chunkSize - 1) * tile;
            Vector2 origin = new Vector2(key.cx * chunkWorld, key.cz * chunkWorld);

            // Pick biome at chunk origin and use its spawner
            var biome = _biomes.GetBiomeAt(origin, _terrain);
            int density = 18; // default count, can be tuned by biome
            var list = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrain, heights, origin, density);

            // Filter to ensure membership in the resolved biome (prevents cross-boundary bleed)
            var filtered = new List<SpawnedObject>(list.Count);
            foreach (var obj in list)
            {
                var at = _biomes.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _terrain);
                if (string.Equals(at.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(obj);
            }
            _cache[key] = filtered;
            return Task.CompletedTask;
        }

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_terrain.TerrainSize - 1) * _terrain.TileSize;
            int cx = (int)MathF.Floor(x / chunkWorld);
            int cz = (int)MathF.Floor(z / chunkWorld);
            return (cx, cz);
        }
    }
}
