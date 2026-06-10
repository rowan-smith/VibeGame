using System.Numerics;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Depletes finite mining voxels and emits item drops with bounded lifetimes.
    /// </summary>
    public class DepleteSystem : ISystem
    {
        private const float ItemDropLifetimeSeconds = 120f;
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private readonly List<(Vector3 Position, ResourceBlockType BlockType)> _pendingDrops = new();

        public DepleteSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            if (_terrain is not IEditableTerrain editable)
                return;

            _pendingDrops.Clear();
            _entities.ForEachWith<DigInteractionComponent, MiningHitComponent>((Entity entity, ref DigInteractionComponent dig, ref MiningHitComponent mining) =>
            {
                if (!dig.IsDigHeld || !mining.HasHit)
                    return;

                float toolPower = MathF.Max(0.1f, dig.ToolBreakSpeedMultiplier);
                if (!editable.TryMineAt(mining.HitPosition, toolPower * MathF.Max(0.01f, dt), out var depletedType))
                    return;
                if (depletedType == ResourceBlockType.None)
                    return;

                _pendingDrops.Add((mining.HitPosition, depletedType));
            });

            foreach (var (position, blockType) in _pendingDrops)
            {
                var drop = _entities.CreateEntity();
                drop.AddComponent(new ItemDropComponent
                {
                    BlockType = blockType,
                    Quantity = 1f
                });
                drop.AddComponent(new TransformComponent
                {
                    Position = position,
                    Rotation = System.Numerics.Quaternion.Identity,
                    Scale = System.Numerics.Vector3.One
                });
                drop.AddComponent(new LifetimeComponent
                {
                    RemainingSeconds = ItemDropLifetimeSeconds
                });
            }
        }
    }
}
