using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    // Reusable no-op spawner for biomes without trees or when placeholders are desired
    public class EmptyTreeSpawner : ITreeSpawner
    {
        public List<(string treeId, Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain, Vector2 originWorld, int chunkSize, int targetCount)
        {
            return new List<(string, Vector3, float, float, float)>();
        }
    }
}
