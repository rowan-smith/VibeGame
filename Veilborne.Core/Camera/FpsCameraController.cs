using System.Numerics;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Core.Settings;

namespace Veilborne.Camera
{
    public class FpsCameraController : ICameraController
    {
        private readonly float _moveSpeed;
        private readonly IInputProvider _input;
        private readonly IGameSettingsService _settings;

        public FpsCameraController(IInputProvider input, IGameSettingsService settings, float moveSpeed = 7.5f)
        {
            _input = input;
            _settings = settings;
            _moveSpeed = moveSpeed;
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

            // Flattened directions for horizontal movement
            Vector3 flatForward = forward;
            flatForward.Y = 0;
            if (flatForward.LengthSquared() > 0.0001f) flatForward = Vector3.Normalize(flatForward);
            Vector3 flatRight = Vector3.Normalize(Vector3.Cross(flatForward, camera.Up));

            var keyboard = _settings.Current.Keyboard;
            var debug = _settings.Current.Debug;
            // Keyboard input — sprint when Left Shift is held
            float speed = _input.IsKeyDown(InputKeys.KEY_LEFT_SHIFT) ? _moveSpeed * 1.75f : _moveSpeed;
            float runMultiplier = RuntimeEnvironment.IsDevelopmentEnvironment ? debug.RunSpeedMultiplier / 100f : 1f;
            speed *= runMultiplier;
            Vector3 horizMoveDir = Vector3.Zero;
            if (KeyBindingTokens.IsDown(_input, keyboard.Forward)) horizMoveDir += flatForward;
            if (KeyBindingTokens.IsDown(_input, keyboard.Backward)) horizMoveDir -= flatForward;
            if (KeyBindingTokens.IsDown(_input, keyboard.Left)) horizMoveDir -= flatRight;
            if (KeyBindingTokens.IsDown(_input, keyboard.Right)) horizMoveDir += flatRight;
            if (horizMoveDir.LengthSquared() > 1e-6f)
                horizMoveDir = Vector3.Normalize(horizMoveDir);

            return horizMoveDir * (speed * dt);
        }
    }
}
