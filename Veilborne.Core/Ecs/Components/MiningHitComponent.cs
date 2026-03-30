using System.Numerics;
using Veilborne.Terrain;

namespace Veilborne.Ecs.Components
{
    public struct MiningHitComponent : IComponent
    {
        public MiningHitComponent() { }

        public bool HasHit { get; set; }
        public Vector3 HitPosition { get; set; }
        public ResourceBlockType BlockType { get; set; }
    }
}
