using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Responsible for generating tree instances for a biome.
    public interface ITreeSpawner
    {
        // Generate deterministic tree instances for a chunk.
        // originWorld is the lower-left world coordinate of the chunk.
        List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            Vector2 originWorld,
            int chunkSize,
            int targetCount);
    }
}
