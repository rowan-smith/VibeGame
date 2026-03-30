using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Interfaces
{
    public interface IInfiniteTerrain : IDebugTerrain
    {
        void UpdateCenter(Vector3 cameraPosition);

        float SampleHeight(Vector3 worldPos);

        void Update();

        void Render(CameraComponent camera);

        void RenderWithExclusions(CameraComponent camera, HashSet<(int cx, int cz)> exclude);
    }
}
