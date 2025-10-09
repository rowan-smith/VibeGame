using System.Numerics;

namespace Veilborne.Core.GameWorlds.Active.Components;

public class PhysicsComponent : Component
{
    // Flat data fields — ECS-style (fast, simple, mutable)
    public Vector3 Velocity; // current velocity in world space
    public Vector3 Acceleration; // accumulated acceleration for this frame
    public bool IsGrounded; // if the entity is on the ground
    public float Mass = 1f; // basic scalar for forces
    public float Drag = 0.1f; // air resistance
    public float GravityScale = 1f; // allows tuning per-entity gravity

    public void ApplyForce(Vector3 force)
    {
        // a = F / m
        Acceleration += force / Mass;
    }

    public void Integrate(float deltaTime)
    {
        // Semi-implicit Euler integration
        Velocity += Acceleration * deltaTime;
        Velocity *= 1f - Drag * deltaTime;
        Acceleration = Vector3.Zero; // clear forces each frame
    }
}
