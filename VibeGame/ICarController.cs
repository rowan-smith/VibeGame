using System.Numerics;

namespace VibeGame
{
    public interface ICarController
    {
        void Reset(in Vector3 startPos, float startYaw = 0f);
        void Update(float dt, out Vector3 position, out float yaw, ITerrainGenerator terrain, float[,] heights,
            System.ReadOnlySpan<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> obstacles);
    }
}
