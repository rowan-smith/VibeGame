using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
    public class CameraComponent : IComponent
    {
        public Vector3 Position { get; set; }

        public Vector3 Target { get; set; }

        public Vector3 Up { get; set; } = Vector3.UnitY;

        public float FovY { get; set; } = 45.0f;

        public bool IsPerspective { get; set; } = true;
    }
}
