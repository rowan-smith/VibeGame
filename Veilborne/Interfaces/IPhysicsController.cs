using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Interfaces
{
    public interface IPhysicsController
    {
        // Applies gravity/jump and moves camera by the provided horizontal displacement.
        // groundHeightFunc should return ground Y for a given (x,z) world point.
        void Integrate(CameraComponent camera, float dt, Vector3 horizontalDisplacement, Func<float, float, float> groundHeightFunc);
    }
}
