using System.Numerics;
using Veilborne.Terrain;

namespace Veilborne.Interfaces
{
    public interface IDebugTerrain
    {
        // Renders chunk bounds for visual debugging
        void RenderDebugChunkBounds(Camera.Camera camera);

        // Returns debug information for the terrain at a world position
        TerrainDebugInfo GetDebugInfo(Vector3 worldPos);
    }
}
