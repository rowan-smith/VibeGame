using System.Numerics;
using Raylib_CsLo;
using VibeGame.Objects;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class ChunkedTerrainService : IInfiniteTerrain
    {
        private readonly ITerrainGenerator _gen;
        private readonly ITerrainRenderer _renderer;
        private readonly ITreeRenderer _treeRenderer;
        private readonly IBiomeProvider _biomeProvider;
        private readonly Dictionary<(int cx, int cz), float[,]> _chunks = new();
        private readonly Dictionary<(int cx, int cz), List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>> _trees = new();
        private readonly int _chunkSize;
        private readonly float _tileSize;
        private int _renderRadiusChunks = 2;

        public ChunkedTerrainService(ITerrainGenerator gen, ITerrainRenderer renderer, ITreeRenderer treeRenderer, IBiomeProvider biomeProvider)
        {
            _gen = gen;
            _renderer = renderer;
            _treeRenderer = treeRenderer;
            _biomeProvider = biomeProvider;
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
                        var heights = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);
                        _chunks[key] = heights;

                        // Generate biome-specific trees for this chunk
                        if (_treeRenderer != null)
                        {
                            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
                            Vector2 origin = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                            var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                            var list = biome.TreeSpawner.GenerateTrees(_gen, origin, _chunkSize, 18);
                            _trees[key] = list;
                        }
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
            var (ccx, ccz) = WorldToChunk(camera.position.X, camera.position.Z);
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

                        // If heights were missing, ensure trees are also generated (biome-specific)
                        if (_treeRenderer != null && !_trees.ContainsKey(key))
                        {
                            Vector2 origin2 = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                            var biome2 = _biomeProvider.GetBiomeAt(origin2, _gen);
                            var list = biome2.TreeSpawner.GenerateTrees(_gen, origin2, _chunkSize, 18);
                            _trees[key] = list;
                        }
                    }
                    Vector2 origin = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                    _renderer.RenderAt(heights, _tileSize, origin, camera, baseColor);

                    // Draw trees for this chunk
                    if (_treeRenderer != null && _trees.TryGetValue(key, out var trees))
                    {
                        // Distance-based culling for trees to improve frame rate
                        float maxTreeDist = 180f; // meters
                        float maxTreeDist2 = maxTreeDist * maxTreeDist;
                        var cam = camera.position;
                        foreach (var t in trees)
                        {
                            float ddx = t.pos.X - cam.X;
                            float ddz = t.pos.Z - cam.Z;
                            if ((ddx * ddx + ddz * ddz) > maxTreeDist2) continue;
                            _treeRenderer.DrawTree(t.pos, t.trunkHeight, t.trunkRadius, t.canopyRadius);
                        }
                    }
                }
            }
        }
    }
}
