namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores current and maximum health values for damageable entities.
/// </summary>
 public struct HealthComponent : IComponent
    {
        public HealthComponent() { }

        public float Current { get; set; } = 100f;

        public float Max { get; set; } = 100f;
    }
}


