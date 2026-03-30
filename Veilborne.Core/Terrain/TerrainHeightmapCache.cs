namespace Veilborne.Core.Terrain
{
    /// <summary>
    /// Bounded heightmap cache with LRU eviction to reduce repeated chunk regeneration.
    /// </summary>
    public sealed class TerrainHeightmapCache
    {
        private readonly object _lock = new();
        private readonly Dictionary<HeightmapCacheKey, float[,]> _entries = new();
        private readonly Dictionary<HeightmapCacheKey, LinkedListNode<HeightmapCacheKey>> _nodes = new();
        private readonly LinkedList<HeightmapCacheKey> _lru = new();
        private readonly int _capacity;

        private readonly record struct HeightmapCacheKey(
            int ChunkX,
            int ChunkZ,
            int ChunkSize,
            int TileMilli,
            int SourceVersion);

        public TerrainHeightmapCache(int capacity = 64)
        {
            _capacity = System.Math.Max(8, capacity);
        }

        public float[,] GetOrCreate(
            (int cx, int cz) key,
            int chunkSize,
            float tileSize,
            int sourceVersion,
            System.Func<float[,]> factory)
        {
            var cacheKey = new HeightmapCacheKey(
                key.cx,
                key.cz,
                chunkSize,
                (int)System.MathF.Round(tileSize * 1000f),
                sourceVersion);

            lock (_lock)
            {
                if (_entries.TryGetValue(cacheKey, out var cached))
                {
                    Touch(cacheKey);
                    return Clone(cached);
                }
            }

            var created = factory();

            lock (_lock)
            {
                if (_entries.TryGetValue(cacheKey, out var existing))
                {
                    Touch(cacheKey);
                    return Clone(existing);
                }

                _entries[cacheKey] = created;
                var node = new LinkedListNode<HeightmapCacheKey>(cacheKey);
                _nodes[cacheKey] = node;
                _lru.AddLast(node);

                while (_entries.Count > _capacity && _lru.First is not null)
                {
                    var evicted = _lru.First.Value;
                    _lru.RemoveFirst();
                    _nodes.Remove(evicted);
                    _entries.Remove(evicted);
                }
            }

            return Clone(created);
        }

        private void Touch(HeightmapCacheKey key)
        {
            if (!_nodes.TryGetValue(key, out var node))
                return;
            _lru.Remove(node);
            _lru.AddLast(node);
        }

        private static float[,] Clone(float[,] src)
        {
            int w = src.GetLength(0);
            int h = src.GetLength(1);
            var copy = new float[w, h];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                copy[x, z] = src[x, z];
            return copy;
        }
    }
}

