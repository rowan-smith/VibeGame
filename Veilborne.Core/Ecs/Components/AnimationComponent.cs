namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores animation playback state for renderable entities.
/// </summary>
 public struct AnimationComponent : IComponent
    {
        public AnimationComponent() { }

        public string Clip { get; set; } = string.Empty;

        public float TimeSeconds { get; set; } = 0f;

        public float Speed { get; set; } = 1f;
    }
}

