using System.Numerics;
using Raylib_cs;

namespace VibeGame.Terrain
{
    public interface ITerrainRenderer
    {
        void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor);

        // New: render a heightmap positioned with its (0,0) corner at originWorld (bottom-left), no centering
        void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera, Color baseColor);
    }
}
