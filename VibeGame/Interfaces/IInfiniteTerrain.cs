using System.Numerics;
using Raylib_CsLo;
using Veilborne.Core.GameWorlds.Terrain;
using VibeGame.Interfaces;

namespace Veilborne.Core.Interfaces
{
    public interface IInfiniteTerrain : IDebugTerrain
    {
        void UpdateCenter(Vector3 cameraPosition);

        float SampleHeight(Vector3 worldPos);

        void Update();

        void Render(Camera camera, Color color);

        void RenderWithExclusions(Camera camera, Color color, HashSet<(int cx, int cz)> exclude);
    }
}
