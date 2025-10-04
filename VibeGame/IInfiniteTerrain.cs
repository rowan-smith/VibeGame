using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public interface IInfiniteTerrain
    {
        // Ensure needed chunks are present around given world position
        void UpdateAround(Vector3 worldPos, int radiusChunks);

        // Sample height at arbitrary world coordinates
        float SampleHeight(float worldX, float worldZ);

        // Render all visible chunks around camera
        void Render(Camera3D camera, Color baseColor);

        // Expose tile and chunk sizes
        float TileSize { get; }
        int ChunkSize { get; }
    }
}