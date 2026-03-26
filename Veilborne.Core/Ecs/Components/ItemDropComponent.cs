using Veilborne.Terrain;

namespace Veilborne.Core.Ecs.Components
{
    public struct ItemDropComponent : IComponent
    {
        public ItemDropComponent() { }

        public ResourceBlockType BlockType { get; set; }
        public float Quantity { get; set; }
    }
}
