using System.Numerics;
using Raylib_cs;

namespace VibeGame.Terrain
{
    public class ChunkedTerrainService : IInfiniteTerrain
    {
        private readonly ITerrainGenerator _gen;
        private readonly ITerrainRenderer _renderer;
        private readonly Dictionary<(int cx, int cz), float[,]> _chunks = new();
        private readonly int _chunkSize;
        private readonly float _tileSize;
        private int _renderRadiusChunks = 2;

        public ChunkedTerrainService(ITerrainGenerator gen, ITerrainRenderer renderer)
        {
            _gen = gen;
            _renderer = renderer;
            _chunkSize = gen.TerrainSize;
            _tileSize = gen.TileSize;
        }

        public float TileSize => _tileSize;
        public int ChunkSize => _chunkSize;

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            int cx = (int)MathF.Floor(x / chunkWorld);
            int cz = (int)MathF.Floor(z / chunkWorld);
            return (cx, cz);
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            _renderRadiusChunks = radiusChunks;
            var (ccx, ccz) = WorldToChunk(worldPos.X, worldPos.Z);
            for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
            {
                for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
                {
                    var key = (ccx + dx, ccz + dz);
                    if (!_chunks.ContainsKey(key))
                    {
                        _chunks[key] = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);
                    }
                }
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // Option 1: sample directly from generator function for precision
            return _gen.ComputeHeight(worldX, worldZ);
        }

        public void Render(Camera3D camera, Color baseColor)
        {
            var (ccx, ccz) = WorldToChunk(camera.Position.X, camera.Position.Z);
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
            for (int dz = -_renderRadiusChunks; dz <= _renderRadiusChunks; dz++)
            {
                for (int dx = -_renderRadiusChunks; dx <= _renderRadiusChunks; dx++)
                {
                    var key = (ccx + dx, ccz + dz);
                    if (!_chunks.TryGetValue(key, out var heights))
                    {
                        heights = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);
                        _chunks[key] = heights;
                    }
                    Vector2 origin = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                    _renderer.RenderAt(heights, _tileSize, origin, camera, baseColor);
                }
            }
        }
    }
}
