using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Core;
using Veilborne.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Executes terrain dig operations from ECS interaction state.
    /// </summary>
    public class DigExecutionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private readonly IWorldConfigService _config;

        public DigExecutionSystem(EntityRegistry entities, IInfiniteTerrain terrain, IWorldConfigService config)
        {
            _entities = entities;
            _terrain = terrain;
            _config = config;
        }

        public void Update(float dt)
        {
            if (_terrain is not IEditableTerrain editable)
                return;

            foreach (var entity in _entities.GetEntitiesWith<DigInteractionComponent>())
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                if (!dig.IsDigHeld || !dig.HasGroundHit)
                    continue;

                float radius = Math.Clamp(_config.Config.DigRadius, 0.2f, 8f);
                float baseStrength = Math.Clamp(_config.Config.DigStrength, 0.1f, 4f);
                float toolMultiplier = Math.Clamp(dig.ToolBreakSpeedMultiplier, 0.1f, 5f);
                float strength = Math.Clamp(baseStrength * toolMultiplier, 0.1f, 8f);
                VoxelFalloff falloff = ParseFalloff(_config.Config.DigFalloff);
                editable.DigSphereAsync(dig.GroundHit, radius, strength, falloff).GetAwaiter().GetResult();
            }
        }

        private static VoxelFalloff ParseFalloff(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return VoxelFalloff.Linear;
            return value.Trim().ToLowerInvariant() switch
            {
                "cosine" => VoxelFalloff.Cosine,
                "exponential" => VoxelFalloff.Exponential,
                _ => VoxelFalloff.Linear,
            };
        }
    }
}

