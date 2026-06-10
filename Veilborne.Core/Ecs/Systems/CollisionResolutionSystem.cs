using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Applies player collision pushback computed during detection.
    /// </summary>
    public class CollisionResolutionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly CollisionFrameBuffer _collisionBuffer;

        public CollisionResolutionSystem(EntityRegistry entities, CollisionFrameBuffer collisionBuffer)
        {
            _entities = entities;
            _collisionBuffer = collisionBuffer;
        }

        public void Update(float dt)
        {
            var push = _collisionBuffer.PlayerPush;
            push.Y = 0f;
            if (push.LengthSquared() < 1e-8f)
                return;

            foreach (var player in _entities.GetEntitiesWith<PlayerComponent>())
            {
                if (!player.TryGetComponent<CameraComponent>(out var cam))
                    continue;

                cam.Position += push;
                cam.Target += push;
                player.SetComponent(cam);

                if (player.TryGetComponent<VelocityComponent>(out var velocity))
                {
                    // Remove into-collider velocity component so movement doesn't jitter against static objects.
                    var pushDir = System.Numerics.Vector3.Normalize(push);
                    var intoSpeed = System.Numerics.Vector3.Dot(velocity.Linear, pushDir);
                    if (intoSpeed < 0f)
                    {
                        velocity.Linear -= pushDir * intoSpeed;
                        player.SetComponent(velocity);
                    }
                }
                break;
            }

            _collisionBuffer.PlayerPush = System.Numerics.Vector3.Zero;
        }
    }
}
