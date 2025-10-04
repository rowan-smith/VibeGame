using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    public interface ITreeRenderer
    {
        // Returns deterministic list of tree instances given a heightmap sampler
        List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            float[,] heights,
            int count);

        void DrawTree(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius);
    }
}
