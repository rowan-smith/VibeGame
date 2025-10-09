using System.Numerics;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.GameWorlds.Active.Components;
using Veilborne.Core.GameWorlds.Active.Entities;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems.Core;

public class PhysicsSystem : ISystem
{
    private const float Gravity = 9.81f;
    private const float JumpForce = 5f;

    public int Priority => 100; // Update order relative to other systems
    public SystemCategory Category => SystemCategory.Physics;
    public bool RunsWhenPaused => false;

    public void Initialize()
    {
    }

    public void Shutdown()
    {
        // Clean up if needed (timers, events, etc.)
    }

    public void Update(GameTime time, GameState state)
    {
        foreach (var entity in state.Entities)
        {
            if (!entity.HasComponent<PhysicsComponent>())
            {
                continue;
            }

            var physics = entity.GetComponent<PhysicsComponent>()!;
            var transform = entity.Transform;

            // Apply gravity
            if (!physics.IsGrounded)
            {
                physics.ApplyForce(Vector3.UnitY * -Gravity * physics.GravityScale);
            }

            // Integrate motion
            physics.Integrate(time.DeltaTime);
            transform.Position += physics.Velocity * time.DeltaTime;

            // Ground collision (simple example)
            if (transform.Position.Y <= 0)
            {
                transform.Position = new Vector3(transform.Position.X, 0, transform.Position.Z);
                physics.IsGrounded = true;
                physics.Velocity.Y = 0;
            }
        }
    }

    public void Jump(Entity entity)
    {
        var physics = entity.GetComponent<PhysicsComponent>();
        if (physics != null && physics.IsGrounded)
        {
            physics.Velocity.Y = JumpForce;
            physics.IsGrounded = false;
        }
    }
}
