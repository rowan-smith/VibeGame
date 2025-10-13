using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using VibeGame.Biomes;
using VibeGame.Core;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
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
            => _cache.TryGetValue(key, out var list) ? list : Array.Empty<SpawnedObject>();

        public void EnsureObjects(Vector3 playerPos, Dictionary<Vector3, Chunk> activeChunks, AsyncTaskQueue async)
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
            int size = _terrain.TerrainSize;
            float tile = _terrain.TileSize;
            float[,] heights = _terrain.GenerateHeightsForChunk(key.cx, key.cz, size);

            float chunkWorld = (size - 1) * tile;
            Vector2 origin = new Vector2(key.cx * chunkWorld, key.cz * chunkWorld);

            var biome = BiomeSampling.GetDominantBiomeForArea(_biomes, _terrain, origin, _terrain.TerrainSize - 1, _terrain.TileSize, 9, 2f);
            int density = 18;
            var spawned = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrain, heights, origin, density);

            var filtered = new List<SpawnedObject>();
            foreach (var obj in spawned)
            {
                var objBiome = _biomes.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _terrain);
                if (string.Equals(objBiome.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(obj);
            }

            _cache[key] = filtered;
            return Task.CompletedTask;
        }

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_terrain.TerrainSize - 1) * _terrain.TileSize;
            return ((int)MathF.Floor(x / chunkWorld), (int)MathF.Floor(z / chunkWorld));
        }
    }
}
