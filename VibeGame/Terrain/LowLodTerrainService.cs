using System;
using System.Collections.Generic;
using System.Numerics;
using ZeroElectric.Vinculum;
using Veilborne.Core.GameWorlds.Terrain;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class LowLodTerrainService
    {
        public float TileSize { get; } = 4.0f;
        public int ChunkSize { get; } = 128;
        public int InnerExclusionRadiusChunks { get; set; }

        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly EditableTerrainService _editable;

        public LowLodTerrainService(EditableTerrainService editable, IBiomeProvider biomeProvider, ITerrainRenderer renderer)
        {
            _editable = editable;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            var desired = new HashSet<(int cx, int cz)>();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                int ring = Math.Max(Math.Abs(x), Math.Abs(z));
                if (ring < InnerExclusionRadiusChunks) continue;
                var key = (centerX + x, centerZ + z);
                desired.Add(key);
                if (!_loadedChunks.ContainsKey(key))
                {
                    float originX = key.Item1 * ChunkSize * TileSize;
                    float originZ = key.Item2 * ChunkSize * TileSize;

                    // Coarsely sample from editable terrain for distant LOD
                    float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                    for (int zz = 0; zz <= ChunkSize; zz++)
                    for (int xx = 0; xx <= ChunkSize; xx++)
                    {
                        float wx = originX + xx * TileSize;
                        float wz = originZ + zz * TileSize;
                        heights[xx, zz] = _editable.SampleHeight(wx, wz);
                    }

                    _loadedChunks[key] = new TerrainChunk
                    {
                        Heights = heights,
                        Origin = new Vector2(originX, originZ),
                        IsMeshGenerated = false,
                        BuiltFromVersion = -1
                    };
                }
            }

            // unload chunks outside desired set
            var toRemove = new List<(int cx, int cz)>();
            foreach (var key in _loadedChunks.Keys)
                if (!desired.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove) _loadedChunks.Remove(key);
        }

        public void Render(Camera3D camera)
        {
            foreach (var kvp in _loadedChunks)
            {
                var chunk = kvp.Value;
                var biome = _biomeProvider.GetBiomeAt(new Vector2(chunk.Origin.X + ChunkSize * 0.5f, chunk.Origin.Y + ChunkSize * 0.5f), null);
                _renderer.ApplyBiomeTextures(biome.Data);
                _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera);
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            foreach (var (cx, cz) in _loadedChunks.Keys)
            {
                Vector3 pos = new(cx * ChunkSize * TileSize, 0, cz * ChunkSize * TileSize);
                Raylib.DrawCubeWires(pos, ChunkSize * TileSize, 0.5f, ChunkSize * TileSize, Raylib.GRAY);
            }
        }
    }
}
