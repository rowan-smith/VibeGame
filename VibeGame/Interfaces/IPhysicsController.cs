using System.Numerics;
using ZeroElectric.Vinculum;

namespace VibeGame
{
    public interface IPhysicsController
    {
        // Applies gravity/jump and moves camera by the provided horizontal displacement.
        // groundHeightFunc should return ground Y for a given (x,z) world point.
        void Integrate(ref Camera3D camera, float dt, Vector3 horizontalDisplacement, Func<float, float, float> groundHeightFunc);
    }
}
