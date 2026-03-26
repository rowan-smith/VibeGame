namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores lightweight enemy AI state and engagement tuning.
/// </summary>
 public struct EnemyComponent : IComponent
    {
        public EnemyComponent() { }

        public int State { get; set; } = 0;

        public float AggroRange { get; set; } = 12f;
    }
}

