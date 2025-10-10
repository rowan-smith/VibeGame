using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_CsLo;

namespace VibeGame.Terrain
{
    public class LowLodTerrainService
    {
        public float TileSize { get; } = 4.0f;
        public int ChunkSize { get; } = 128;
        public int InnerExclusionRadiusChunks { get; set; }

        private readonly HashSet<(int cx, int cz)> _loaded = new();

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

        public void Render(Camera3D camera, Color baseColor)
        {
            foreach (var (cx, cz) in _loaded)
            {
                float worldX = cx * ChunkSize * TileSize;
                float worldZ = cz * ChunkSize * TileSize;
                float y = MathF.Sin(worldX * 0.01f) * MathF.Cos(worldZ * 0.01f) * 8f;
                Raylib.DrawCube(new Vector3(worldX, y, worldZ), ChunkSize, 1f, ChunkSize, Raylib.DARKGRAY);
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            foreach (var (cx, cz) in _loaded)
            {
                Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.5f, ChunkSize * TileSize, Raylib.GRAY);
            }
        }
    }
}
