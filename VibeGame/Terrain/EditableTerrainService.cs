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

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly HashSet<(int cx, int cz)> _loaded = new();
        private readonly object _lock = new();

        public EditableTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer)
        {
            _biomeProvider = biomeProvider;
            _renderer = renderer;
        }

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

        public void Render(Camera3D camera)
        {
            if (!RenderBaseHeightmap) return;

            lock (_lock)
            {
                foreach (var (cx, cz) in _loaded)
                {
                    float worldX = cx * ChunkSize * TileSize;
                    float worldZ = cz * ChunkSize * TileSize;

                    // Create full heightmap grid for this chunk
                    float[,] heights = new float[ChunkSize, ChunkSize];
                    for (int z = 0; z < ChunkSize; z++)
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        float wx = worldX + x * TileSize;
                        float wz = worldZ + z * TileSize;
                        heights[x, z] = SampleHeight(wx, wz);
                    }

                    // Sample biome in center of chunk for texture/color
                    Vector2 center = new(worldX + ChunkSize * TileSize * 0.5f, worldZ + ChunkSize * TileSize * 0.5f);
                    var biome = _biomeProvider.GetBiomeAt(center, null); // <-- fixed call signature

                    // Apply biome textures and color tint
                    _renderer.ApplyBiomeTextures(biome.Data);
                    _renderer.SetColorTint(TerrainRenderer.ToRaylibColor(biome.Data.Color)); // optional if you want per-biome tint

                    // Render full grid
                    _renderer.RenderAt(
                        heights: heights,
                        tileSize: TileSize,
                        originWorld: new Vector2(worldX, worldZ),
                        camera: camera
                    );
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
                    Raylib_CsLo.Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.2f, ChunkSize * TileSize, Raylib_CsLo.Raylib.RED);
                }
            }
        }

        public async Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
            => await Task.Delay(10);

        public async Task PlaceSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
            => await Task.Delay(10);

        public Task PumpAsyncJobs() => Task.CompletedTask;
    }
}
