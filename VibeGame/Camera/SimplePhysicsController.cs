using System.Numerics;
using ZeroElectric.Vinculum;

namespace VibeGame
{
    public class SimplePhysicsController : IPhysicsController
    {
        private float _verticalVelocity = 0f;
        private bool _isGrounded = false;
        private readonly float _gravity;
        private readonly float _jumpSpeed;
        private readonly float _eyeHeight;

        public SimplePhysicsController(float gravity = -20f, float jumpSpeed = 8.5f, float eyeHeight = 1.7f)
        {
            _gravity = gravity;
            _jumpSpeed = jumpSpeed;
            _eyeHeight = eyeHeight;
        }

        public void Integrate(ref Camera3D camera, float dt, Vector3 horizontalDisplacement, Func<float, float, float> groundHeightFunc)
        {
            // Track initial position to preserve camera forward by applying same delta to target
            Vector3 startPos = camera.position;

            // Horizontal move first (horizontalDisplacement is already scaled by dt)
            camera.position += new Vector3(horizontalDisplacement.X, 0, horizontalDisplacement.Z);

            // Ground height under current position (eye height applied after sampling)
            float groundY = groundHeightFunc(camera.position.X, camera.position.Z) + _eyeHeight;

            // Jump input only when grounded
            if (_isGrounded && Raylib.IsKeyPressed(KeyboardKey.KEY_SPACE))
            {
                _verticalVelocity = _jumpSpeed;
                _isGrounded = false;
            }

            // Integrate vertical velocity
            _verticalVelocity += _gravity * dt;
            camera.position = camera.position with
            {
                Y = camera.position.Y + _verticalVelocity * dt,
            };

            // Ground collision
            groundY = groundHeightFunc(camera.position.X, camera.position.Z) + _eyeHeight;
            if (camera.position.Y <= groundY)
            {
                camera.position = new Vector3(camera.position.X, groundY, camera.position.Z);
                _verticalVelocity = 0f;
                _isGrounded = true;
            }
            else
            {
                _isGrounded = false;
            }

            // Apply same positional delta to target to avoid fighting camera controller orientation
            Vector3 delta = camera.position - startPos;
            camera.target += delta;
        }
    }
}
