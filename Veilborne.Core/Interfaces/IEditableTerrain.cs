using System.Numerics;
using Veilborne.Terrain;

namespace Veilborne.Interfaces
{
    public interface IEditableTerrain : IInfiniteTerrain
    {
        Task DigSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);

        Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);
    }
}
