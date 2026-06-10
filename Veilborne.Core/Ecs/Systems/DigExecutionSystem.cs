using System.Numerics;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Executes terrain dig operations from ECS interaction state.
    /// </summary>
    public class DigExecutionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private readonly IWorldConfigService _config;
        private readonly IRandomSource _random;
        private float _accumulatedSeconds;
        private bool _wasDigging;
        private const float DigCommandTickSeconds = 1f / 40f;
        private readonly List<(Vector3 hitPos, DigInteractionComponent dig)> _pendingParticles = new();

        public DigExecutionSystem(
            EntityRegistry entities,
            IInfiniteTerrain terrain,
            IWorldConfigService config,
            IRandomSource random)
        {
            _entities = entities;
            _terrain = terrain;
            _config = config;
            _random = random;
        }

        public void Update(float dt)
        {
            if (_terrain is not IEditableTerrain editable)
                return;

            _accumulatedSeconds += MathF.Max(0f, dt);
            bool anyDigQueued = false;
            _pendingParticles.Clear();
            _entities.ForEachWith<DigInteractionComponent>(entity =>
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                bool canDig = dig.IsDigHeld && dig.HasGroundHit;
                if (!canDig)
                    return;

                bool justStarted = !_wasDigging;
                if (!justStarted && _accumulatedSeconds < DigCommandTickSeconds)
                    return;

                float radius = Math.Clamp(_config.Config.Dig.Radius, 0.2f, 8f);
                float baseStrength = Math.Clamp(_config.Config.Dig.Strength, 0.1f, 4f);
                float toolMultiplier = Math.Clamp(dig.ToolBreakSpeedMultiplier, 0.1f, 5f);
                float strength = Math.Clamp(baseStrength * toolMultiplier, 0.1f, 8f);
                VoxelFalloff falloff = ParseFalloff(_config.Config.Dig.Falloff);
                _ = editable.DigSphereAsync(dig.GroundHit, radius, strength, falloff);
                anyDigQueued = true;

                if (_config.Config.Dig.SpawnParticles)
                    _pendingParticles.Add((dig.GroundHit, dig));
            });

            foreach (var (hitPos, dig) in _pendingParticles)
                SpawnDigParticles(hitPos, dig);

            _wasDigging = anyDigQueued;
            if (anyDigQueued)
                _accumulatedSeconds = 0f;
        }

        private void SpawnDigParticles(Vector3 hitPos, DigInteractionComponent dig)
        {
            int count = Math.Clamp(_config.Config.Dig.ParticlesPerDig, 0, 20);
            float lifetime = MathF.Max(0.1f, _config.Config.Dig.ParticleLifetime);

            ResourceBlockType blockType = ResourceBlockType.Dirt;
            if (dig.HasGroundHit)
            {
                float surfaceY = hitPos.Y;
                if (surfaceY > -0.3f) blockType = ResourceBlockType.Grass;
                else if (surfaceY > -1.0f) blockType = ResourceBlockType.Dirt;
                else blockType = ResourceBlockType.Rock;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = (float)(_random.NextDouble() * Math.PI * 2);
                float speed = 0.5f + (float)_random.NextDouble() * 1.5f;
                float upSpeed = 1.0f + (float)_random.NextDouble() * 2.0f;
                var velocity = new Vector3(
                    MathF.Cos(angle) * speed,
                    upSpeed,
                    MathF.Sin(angle) * speed);

                var particleEntity = _entities.CreateEntity();
                particleEntity.AddComponent(new TransformComponent
                {
                    Position = hitPos + new Vector3(0f, 0.1f, 0f),
                    Scale = new Vector3(0.1f, 0.1f, 0.1f)
                });
                particleEntity.AddComponent(new DigParticleComponent
                {
                    Velocity = velocity,
                    Lifetime = lifetime * (0.6f + (float)_random.NextDouble() * 0.8f),
                    Gravity = 9.8f,
                    BlockType = blockType
                });
                particleEntity.AddComponent(new RenderComponent
                {
                    Visible = true,
                    IsFoliage = true
                });
            }
        }

        private static VoxelFalloff ParseFalloff(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return VoxelFalloff.Stepped;
            return value.Trim().ToLowerInvariant() switch
            {
                "cosine" => VoxelFalloff.Cosine,
                "exponential" => VoxelFalloff.Exponential,
                "stepped" => VoxelFalloff.Stepped,
                "linear" => VoxelFalloff.Linear,
                _ => VoxelFalloff.Stepped,
            };
        }
    }
}
