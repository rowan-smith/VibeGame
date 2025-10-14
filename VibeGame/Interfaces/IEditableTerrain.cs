using System.Numerics;
using ZeroElectric.Vinculum;
using Veilborne.Core.GameWorlds.Terrain;
using VibeGame.Terrain;

namespace Veilborne.Core.Interfaces
{
    public interface IEditableTerrain : IInfiniteTerrain
    {
        Task DigSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);

        Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff);
    }
}
