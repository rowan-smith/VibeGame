using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class ReadOnlyTerrainService : ITerrainColliderProvider
    {
        public float TileSize { get; } = 2.0f;
        public int ChunkSize { get; } = 64;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly ITerrainGenerator _generator;
        private readonly HashSet<(int cx, int cz)> _loaded = new();

        public ReadOnlyTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator generator)
        {
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _generator = generator;
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            _loaded.Clear();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
                _loaded.Add((centerX + x, centerZ + z));
        }

        public void RenderTiles(Camera3D camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            foreach (var (cx, cz) in _loaded)
            {
                if (exclude != null && exclude.Contains((cx, cz))) continue;

                float originX = cx * ChunkSize * TileSize;
                float originZ = cz * ChunkSize * TileSize;
                var heights = _generator.GenerateHeightsForChunk(cx, cz, ChunkSize);
                var biome = _biomeProvider.GetBiomeAt(new Vector2(originX + ChunkSize * 0.5f, originZ + ChunkSize * 0.5f), _generator);

                _renderer.ApplyBiomeTextures(biome.Data);
                _renderer.RenderAt(heights, TileSize, new Vector2(originX, originZ), camera);
            }
        }

        public void Render(Camera3D camera) => RenderTiles(camera);

        public void RenderWithExclusions(Camera3D camera, HashSet<(int cx, int cz)> exclusions) => RenderTiles(camera, exclusions);

        public float SampleHeight(float worldX, float worldZ)
            => MathF.Sin(worldX * 0.05f) * MathF.Cos(worldZ * 0.05f) * 5f;

        public IBiome GetBiomeAt(float worldX, float worldZ)
            => _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), null);

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            foreach (var (cx, cz) in _loaded)
            {
                Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                Raylib_CsLo.Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.2f, ChunkSize * TileSize, Raylib_CsLo.Raylib.DARKGREEN);
            }
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            yield return (worldPos + new Vector2(10, 0), 5);
            yield return (worldPos + new Vector2(-8, 7), 3);
        }
    }
}
