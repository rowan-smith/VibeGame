using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores per-frame dig interaction intent and ground-hit probe results.
/// </summary>
 public struct DigInteractionComponent : IComponent
    {
        public DigInteractionComponent() { }

        public bool IsDigHeld { get; set; } = false;

        public bool HasGroundHit { get; set; } = false;

        public Vector3 GroundHit { get; set; } = Vector3.Zero;

        public float ProbeMaxDistance { get; set; } = 6f;

        public float ProbeStep { get; set; } = 0.25f;

        public float ProbeEpsilon { get; set; } = 0.05f;

        public float ToolBreakSpeedMultiplier { get; set; } = 1f;

        public int ToolStaminaCost { get; set; } = 0;
    }
}

