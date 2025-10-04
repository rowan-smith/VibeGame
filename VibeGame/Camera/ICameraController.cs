using System.Numerics;
using Raylib_CsLo;

namespace VibeGame
{
    public interface ICameraController
    {
        // Updates camera orientation from input and returns desired horizontal movement delta (in world units) for this frame
        Vector3 UpdateAndGetHorizontalMove(ref Camera3D camera, float dt);
    }
}
