using System.Numerics;
using Raylib_CsLo;

namespace Veilborne.Core.GameWorlds.Terrain
{
    public class TerrainChunk
    {
        public Vector3 Origin { get; }
        public int Size { get; }
        public float TileSize { get; }

        public TerrainChunk(int size, float tileSize)
        {
            Size = size;
            TileSize = tileSize;
            Origin = Vector3.Zero;
        }

        public void Render(Camera camera, Color color)
        {
            // Simple debug visualization — draws a grid square per chunk
            var chunkWorldSize = (Size - 1) * TileSize;
            Raylib.DrawCubeWires(
                new Vector3(Origin.X + chunkWorldSize / 2, 0, Origin.Z + chunkWorldSize / 2),
                chunkWorldSize,
                0.1f,
                chunkWorldSize,
                color
            );
        }
    }
}
