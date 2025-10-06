using System.Numerics;
using System.Collections.Generic;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public interface IInfiniteTerrain
    {
        // Ensure needed chunks are present around given world position
        void UpdateAround(Vector3 worldPos, int radiusChunks);

        // Sample height at arbitrary world coordinates
        float SampleHeight(float worldX, float worldZ);

        // Query biome at a world position
        IBiome GetBiomeAt(float worldX, float worldZ);

        // Render all visible chunks around camera
        void Render(Camera3D camera, Color baseColor);

        // Optional debug rendering (e.g., chunk bounds)
        void RenderDebugChunkBounds(Camera3D camera);

        // Query simple XZ circle colliders for nearby solid world objects (e.g., trees)
        IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range);

        // Expose tile and chunk sizes
        float TileSize { get; }
        int ChunkSize { get; }
    }
}
