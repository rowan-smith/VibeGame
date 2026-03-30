using System.Numerics;
using Veilborne.Core.Terrain;

namespace Veilborne.Core.Interfaces
{
    public interface IEditableTerrain : IInfiniteTerrain
    {
        Task DigSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);

        Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);

        bool TryMineAt(Vector3 position, float power, out ResourceBlockType blockType);
    }
}
