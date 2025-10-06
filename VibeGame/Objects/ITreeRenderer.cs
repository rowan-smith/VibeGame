using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    public interface ITreeRenderer
    {
        // Returns deterministic list of tree instances in world space for a given chunk origin
        List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count);

        void DrawTree(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius);
    }
}
