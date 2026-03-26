namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores simple collision bounds data used by gameplay and physics queries.
/// </summary>
 public struct ColliderComponent : IComponent
    {
        public ColliderComponent() { }

        public float Radius { get; set; } = 0f;
    }
}


