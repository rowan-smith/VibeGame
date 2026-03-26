using System;

namespace Veilborne.Core.Ecs.Components
{
    /// <summary>
    /// Defines collision layer and mask filtering rules for an entity.
    /// </summary>
    [Flags]
    public enum CollisionLayer
    {
        None = 0,
        Player = 1 << 0,
        WorldStatic = 1 << 1,
        Foliage = 1 << 2
    }

    /// <summary>
    /// Stores collision filtering state used by collision systems.
    /// </summary>
    public struct CollisionFilterComponent : IComponent
    {
        public CollisionFilterComponent() { }

        public CollisionLayer Layer { get; set; } = CollisionLayer.None;

        public CollisionLayer CollidesWith { get; set; } = CollisionLayer.None;
    }
}

