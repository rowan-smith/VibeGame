namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Rebuilds the world-object spatial index at a fixed cadence for stable collision cost.
    /// </summary>
    public sealed class WorldObjectSpatialIndexSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly WorldObjectSpatialIndex _index;
        private float _accumulatedSeconds;
        private const float RebuildIntervalSeconds = 1f / 8f;

        public WorldObjectSpatialIndexSystem(EntityRegistry entities, WorldObjectSpatialIndex index)
        {
            _entities = entities;
            _index = index;
            _accumulatedSeconds = RebuildIntervalSeconds;
        }

        public void Update(float dt)
        {
            _accumulatedSeconds += MathF.Max(0f, dt);
            if (_accumulatedSeconds < RebuildIntervalSeconds)
                return;

            _accumulatedSeconds = 0f;
            _index.Rebuild(_entities);
        }
    }
}
