using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Core.Settings;

namespace Veilborne.Camera
{
    public class FpsCameraController : ICameraController
    {
        private readonly IInputProvider _input;
        private readonly IGameSettingsService _settings;

        public FpsCameraController(IInputProvider input, IGameSettingsService settings)
        {
            _input = input;
            _settings = settings;
        }

        public Vector3 UpdateAndGetHorizontalMove(ref CameraComponent camera, float dt)
        {
            // Mouse look
            Vector2 mouseDelta = _input.GetMouseDelta();
            var general = _settings.Current.General;
            float mouseSensitivity = general.MouseSensitivity;
            float mouseYSign = general.InvertMouseY ? 1f : -1f;
            Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

            // Yaw around global up
            Matrix4x4 yaw = Matrix4x4.CreateFromAxisAngle(camera.Up, -mouseDelta.X * mouseSensitivity);
            forward = Vector3.TransformNormal(forward, yaw);
            right = Vector3.TransformNormal(right, yaw);

            // Pitch around right axis with clamp
            Vector3 pitchAxis = right;
            Matrix4x4 pitch = Matrix4x4.CreateFromAxisAngle(pitchAxis, mouseDelta.Y * mouseYSign * mouseSensitivity);
            Vector3 newForward = Vector3.TransformNormal(forward, pitch);
            float yDot = Vector3.Dot(newForward, camera.Up);
            if (yDot > -0.95f && yDot < 0.95f) forward = newForward;

            // Update camera target
            camera.Target = camera.Position + forward;

            // Movement intent is handled by ECS input systems.
            return Vector3.Zero;
        }
    }
}
