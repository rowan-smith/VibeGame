using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Interfaces
{
    public interface IDebugTerrain
    {
        // Renders chunk bounds for visual debugging
        void RenderDebugChunkBounds(Veilborne.Core.GameWorlds.Terrain.Camera camera);

        // Returns debug information for the terrain at a world position
        TerrainDebugInfo GetDebugInfo(Vector3 worldPos);
    }
}
