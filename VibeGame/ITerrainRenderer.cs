using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public interface ITerrainRenderer
    {
        void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor);
    }
}
