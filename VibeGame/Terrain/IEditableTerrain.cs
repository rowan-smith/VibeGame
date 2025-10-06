using System.Numerics;
using System.Threading.Tasks;

namespace VibeGame.Terrain
{
    // Optional extension interface for hybrid, editable terrain
    public interface IEditableTerrain
    {
        // Smooth dig/add material using a spherical brush with falloff
        Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine);

        // Allows engine to pump any background jobs if needed
        void PumpAsyncJobs();
    }
}
