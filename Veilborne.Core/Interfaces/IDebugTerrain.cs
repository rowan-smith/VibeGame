using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Terrain;

namespace Veilborne.Core.Interfaces
{
    public interface IDebugTerrain
    {
        // Renders chunk bounds for visual debugging
        void RenderDebugChunkBounds(CameraComponent camera);

        // Returns debug information for the terrain at a world position
        TerrainDebugInfo GetDebugInfo(Vector3 worldPos);
    }
}
