using System.Numerics;
using ZeroElectric.Vinculum;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public interface ITerrainRenderer
    {
        void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor);

        // Render a heightmap positioned with its (0,0) corner at originWorld (bottom-left), no centering
        void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera);

        // Apply the biome-specific surface texture set prior to rendering
        void ApplyBiomeTextures(BiomeData biome);

        void SetColorTint(Color color);

        void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld);
    }
}
