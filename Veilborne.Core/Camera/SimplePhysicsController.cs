using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Camera
{
    public class SimplePhysicsController : IPhysicsController
    {
        private readonly float _eyeHeight;

        public SimplePhysicsController(float eyeHeight = 1.7f)
        {
            _eyeHeight = eyeHeight;
        }

        public void Integrate(ref CameraComponent camera, ref JumpComponent jump, ref VerticalVelocityComponent verticalVelocity, float dt, Vector3 horizontalDisplacement, float gravityY, Func<float, float, float> groundHeightFunc)
        {
            // Track initial position to preserve camera forward by applying same delta to target
            Vector3 startPos = camera.Position;

            // Horizontal move first (horizontalDisplacement is already scaled by dt)
            camera.Position += new Vector3(horizontalDisplacement.X, 0, horizontalDisplacement.Z);

            // Ground height under current position (eye height applied after sampling)
            float groundY = groundHeightFunc(camera.Position.X, camera.Position.Z) + _eyeHeight;

            jump.JumpBufferTimer = MathF.Max(0f, jump.JumpBufferTimer - dt);

            if (jump.IsGrounded)
                jump.CoyoteTimer = jump.CoyoteSeconds;
            else
                jump.CoyoteTimer = MathF.Max(0f, jump.CoyoteTimer - dt);

            if (jump.JumpBufferTimer > 0f && jump.CoyoteTimer > 0f)
            {
                verticalVelocity.Value = jump.JumpSpeed;
                jump.IsGrounded = false;
                jump.JumpBufferTimer = 0f;
                jump.CoyoteTimer = 0f;
            }

            // Integrate vertical velocity
            verticalVelocity.Value += gravityY * dt;
            camera.Position = camera.Position with
            {
                Y = camera.Position.Y + verticalVelocity.Value * dt,
            };

            // Ground collision
            groundY = groundHeightFunc(camera.Position.X, camera.Position.Z) + _eyeHeight;
            if (camera.Position.Y <= groundY)
            {
                camera.Position = new Vector3(camera.Position.X, groundY, camera.Position.Z);
                verticalVelocity.Value = 0f;
                jump.IsGrounded = true;
            }
            else
            {
                jump.IsGrounded = false;
            }

            // Apply same positional delta to target to avoid fighting camera controller orientation
            Vector3 delta = camera.Position - startPos;
            camera.Target += delta;
        }
    }
}
