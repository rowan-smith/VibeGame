using System.Collections.Concurrent;
using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Manages biome assignment per chunk and provides lookup helpers.
    public sealed class BiomeManager
    {
        private readonly IBiomeProvider _provider;
        private readonly ITerrainGenerator _terrain;
        private readonly ConcurrentDictionary<(int cx, int cz), IBiome> _cache = new();
        private readonly int _chunkSize;
        private readonly float _tileSize;

        public int Seed { get; }

        public BiomeManager(int seed, IBiomeProvider provider, ITerrainGenerator terrain)
        {
            Seed = seed;
            _provider = provider;
            _terrain = terrain;
            _chunkSize = terrain.TerrainSize;
            _tileSize = terrain.TileSize;
        }

        public IBiome GetBiomeAt(float x, float z)
        {
            return _provider.GetBiomeAt(new Vector2(x, z), _terrain);
        }

        public IBiome GetBiomeAt(Vector2 pos)
        {
            return _provider.GetBiomeAt(pos, _terrain);
        }

        public void EnsureChunks(Vector3 playerPos, VibeGame.Core.AsyncTaskQueue async)
        {
            var (ccx, ccz) = WorldToChunk(playerPos.X, playerPos.Z);
            int radius = 4; // modest prewarm radius; real value may come from config
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var key = (ccx + dx, ccz + dz);
                if (_cache.ContainsKey(key)) continue;
                float chunkWorld = (_chunkSize - 1) * _tileSize;
                Vector2 origin = new Vector2(key.Item1 * chunkWorld, key.Item2 * chunkWorld);
                async.Enqueue(() =>
                {
                    var biome = _provider.GetBiomeAt(origin, _terrain);
                    _cache.TryAdd(key, biome);
                    return Task.CompletedTask;
                });
            }
        }

        public bool TryGetCachedBiomeForChunk((int cx, int cz) key, out IBiome biome)
            => _cache.TryGetValue(key, out biome!);

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            int cx = (int)MathF.Floor(x / chunkWorld);
            int cz = (int)MathF.Floor(z / chunkWorld);
            return (cx, cz);
        }
    }
}
