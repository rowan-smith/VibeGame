using System.Numerics;

namespace Veilborne.Camera
{
    public class Camera
    {
        public Vector3 Position { get; private set; }
        public Vector3 Target { get; private set; }
        public Vector3 Up { get; }
        public float Fov { get; }

        public Camera(Vector3 position, Vector3 target, Vector3 up, float fov = 60f)
        {
            Position = position;
            Target = target;
            Up = up;
            Fov = fov;
        }

        public void UpdateFromBackend()
        {
            // Placeholder for legacy wrapper compatibility.
        }
    }
}
