namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores basic particle emitter counters and spawn rates.
/// </summary>
 public struct ParticleEmitterComponent : IComponent
    {
        public ParticleEmitterComponent() { }

        public int LiveCount { get; set; } = 0;

        public int MaxCount { get; set; } = 256;

        public float SpawnRate { get; set; } = 0f;

        public float SpawnAccumulator { get; set; } = 0f;
    }
}

