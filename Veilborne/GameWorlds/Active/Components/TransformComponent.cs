using System.Numerics;
using Veilborne.Utility;

namespace Veilborne.GameWorlds.Active.Components
{
    public class TransformComponent : Component
    {
        public Vector3 Position { get; set; } = Vector3.Zero;

        public Vector3 Rotation { get; set; } = Vector3.Zero;

        public Vector3 Scale { get; set; } = new Vector3(1, 1, 1);

        public override void Update(GameTime time)
        {
            // Example: nothing yet, placeholder
        }
    }
}
