using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class LowLodTerrainService
    {
        public float TileSize { get; } = 4.0f;
        public int ChunkSize { get; } = 128;
        public int InnerExclusionRadiusChunks { get; set; }

        private readonly HashSet<(int cx, int cz)> _loaded = new();

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly ITerrainGenerator _generator;

        public LowLodTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator generator)
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
            {
                int ring = Math.Max(Math.Abs(x), Math.Abs(z));
                if (ring < InnerExclusionRadiusChunks) continue;
                _loaded.Add((centerX + x, centerZ + z));
            }
        }

        public void Render(Camera3D camera)
        {
            foreach (var (cx, cz) in _loaded)
            {
                float originX = cx * ChunkSize * TileSize;
                float originZ = cz * ChunkSize * TileSize;

                var heights = _generator.GenerateHeightsForChunk(cx, cz, ChunkSize);
                var biome = _biomeProvider.GetBiomeAt(new Vector2(originX + ChunkSize * 0.5f, originZ + ChunkSize * 0.5f), _generator);

                _renderer.ApplyBiomeTextures(biome.Data);
                _renderer.RenderAt(heights, TileSize, new Vector2(originX, originZ), camera);
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            foreach (var (cx, cz) in _loaded)
            {
                Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                Raylib_CsLo.Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.5f, ChunkSize * TileSize, Raylib_CsLo.Raylib.GRAY);
            }
        }
    }
}
