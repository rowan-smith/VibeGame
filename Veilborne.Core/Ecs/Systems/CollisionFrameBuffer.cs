using System.Numerics;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Per-frame transient collision data shared between detection and resolution phases.
    /// </summary>
    public class CollisionFrameBuffer
    {
        public Vector3 PlayerPush { get; set; } = Vector3.Zero;
    }
}
