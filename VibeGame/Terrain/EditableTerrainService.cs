using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class EditableTerrainService
    {
        public bool RenderBaseHeightmap { get; set; } = true;
        public float TileSize { get; } = 1.0f;
        public int ChunkSize { get; } = 32;

        private readonly HashSet<(int cx, int cz)> _loaded = new();
        private readonly object _lock = new();

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            lock (_lock)
            {
                _loaded.Clear();
                for (int z = -radiusChunks; z <= radiusChunks; z++)
                    for (int x = -radiusChunks; x <= radiusChunks; x++)
                        _loaded.Add((centerX + x, centerZ + z));
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            return MathF.Sin(worldX * 0.1f) * MathF.Cos(worldZ * 0.1f) * 2f;
        }

        public void Render(Camera3D camera, Color baseColor)
        {
            if (!RenderBaseHeightmap)
                return;
            lock (_lock)
            {
                foreach (var (cx, cz) in _loaded)
                {
                    float worldX = cx * ChunkSize * TileSize;
                    float worldZ = cz * ChunkSize * TileSize;
                    float y = SampleHeight(worldX, worldZ);
                    Raylib.DrawCube(new Vector3(worldX, y, worldZ), ChunkSize, 1, ChunkSize, baseColor);
                }
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            lock (_lock)
            {
                foreach (var (cx, cz) in _loaded)
                {
                    Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                    Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.2f, ChunkSize * TileSize, Raylib.RED);
                }
            }
        }

        public async Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            // Simulate async voxel edit
            await Task.Delay(10);
        }
        
        public async Task PlaceSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            // Simulate async voxel edit
            await Task.Delay(10);
        }

        public Task PumpAsyncJobs() => Task.CompletedTask;
    }
}
