using System.Numerics;

namespace Veilborne.Interfaces
{
    public interface IInfiniteTerrain : IDebugTerrain
    {
        void UpdateCenter(Vector3 cameraPosition);

        float SampleHeight(Vector3 worldPos);

        void Update();

        void Render(Camera.Camera camera);

        void RenderWithExclusions(Camera.Camera camera, HashSet<(int cx, int cz)> exclude);
    }
}
