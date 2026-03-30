namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores jump tuning and transient jump state.
/// </summary>
 public struct JumpComponent : IComponent
    {
        public JumpComponent() { }

        public float JumpSpeed { get; set; } = 8.5f;

        public float JumpBufferSeconds { get; set; } = 0.12f;

        public float CoyoteSeconds { get; set; } = 0.10f;

        public float JumpBufferTimer { get; set; }

        public float CoyoteTimer { get; set; }

        public bool IsGrounded { get; set; }
    }
}


