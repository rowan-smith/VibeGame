using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Interfaces
{
    public interface ICameraController
    {
        // Updates camera orientation from input and returns desired horizontal movement delta (in world units) for this frame
        Vector3 UpdateAndGetHorizontalMove(ref CameraComponent camera, float dt);
    }
}
