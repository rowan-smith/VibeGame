using System.Numerics;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.GameWorlds.Terrain
{
    public class Camera
    {
        public Camera3D RaylibCamera;

        public Camera(Vector3 position, Vector3 target, Vector3 up, float fov = 60f)
        {
            RaylibCamera = new Camera3D(position, target, up, fov, CameraProjection.CAMERA_PERSPECTIVE);
        }

        public Vector3 Position => RaylibCamera.position;
        public Vector3 Target => RaylibCamera.target;

        public void UpdateFromRaylib()
        {
            Raylib.UpdateCamera(ref RaylibCamera, CameraMode.CAMERA_FIRST_PERSON);
        }
    }
}
