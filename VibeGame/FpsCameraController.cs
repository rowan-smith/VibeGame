using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public class FpsCameraController : ICameraController
    {
        private readonly float _moveSpeed;
        private readonly float _mouseSensitivity;

        public FpsCameraController(float moveSpeed = 7.5f, float mouseSensitivity = 0.0035f)
        {
            _moveSpeed = moveSpeed;
            _mouseSensitivity = mouseSensitivity;
        }

        public Vector3 UpdateAndGetHorizontalMove(ref Camera3D camera, float dt)
        {
            // Mouse look
            Vector2 mouseDelta = Raylib.GetMouseDelta();
            Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

            // Yaw around global up
            Matrix4x4 yaw = Matrix4x4.CreateFromAxisAngle(camera.Up, -mouseDelta.X * _mouseSensitivity);
            forward = Vector3.TransformNormal(forward, yaw);
            right = Vector3.TransformNormal(right, yaw);

            // Pitch around right axis with clamp
            Vector3 pitchAxis = right;
            Matrix4x4 pitch = Matrix4x4.CreateFromAxisAngle(pitchAxis, -mouseDelta.Y * _mouseSensitivity);
            Vector3 newForward = Vector3.TransformNormal(forward, pitch);
            float yDot = Vector3.Dot(newForward, camera.Up);
            if (yDot > -0.95f && yDot < 0.95f) forward = newForward;

            // Update camera target
            camera.Target = camera.Position + forward;

            // Flattened directions for horizontal movement
            Vector3 flatForward = forward; flatForward.Y = 0;
            if (flatForward.LengthSquared() > 0.0001f) flatForward = Vector3.Normalize(flatForward);
            Vector3 flatRight = Vector3.Normalize(Vector3.Cross(flatForward, camera.Up));

            // Keyboard input
            Vector3 horizMoveDir = Vector3.Zero;
            if (Raylib.IsKeyDown(KeyboardKey.W)) horizMoveDir += flatForward;
            if (Raylib.IsKeyDown(KeyboardKey.S)) horizMoveDir -= flatForward;
            if (Raylib.IsKeyDown(KeyboardKey.A)) horizMoveDir -= flatRight;
            if (Raylib.IsKeyDown(KeyboardKey.D)) horizMoveDir += flatRight;
            if (horizMoveDir.LengthSquared() > 1e-6f)
                horizMoveDir = Vector3.Normalize(horizMoveDir);

            return horizMoveDir * (_moveSpeed * dt);
        }
    }
}