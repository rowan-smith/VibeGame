using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Depletes finite mining voxels and emits item drops/dirty patch tags.
    /// </summary>
    public class DepleteSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;

        public DepleteSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            if (_terrain is not IEditableTerrain editable)
                return;

            foreach (var entity in _entities.GetEntitiesWith<DigInteractionComponent, MiningHitComponent>())
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                var mining = entity.GetComponent<MiningHitComponent>();
                if (!dig.IsDigHeld || !mining.HasHit)
                    continue;

                float toolPower = MathF.Max(0.1f, dig.ToolBreakSpeedMultiplier);
                if (!editable.TryMineAt(mining.HitPosition, toolPower * MathF.Max(0.01f, dt), out var depletedType))
                    continue;
                if (depletedType == ResourceBlockType.None)
                    continue;

                var drop = _entities.CreateEntity();
                drop.AddComponent(new ItemDropComponent
                {
                    BlockType = depletedType,
                    Quantity = 1f
                });
                drop.AddComponent(new TransformComponent
                {
                    Position = mining.HitPosition,
                    Rotation = System.Numerics.Quaternion.Identity,
                    Scale = System.Numerics.Vector3.One
                });

                var dirty = _entities.CreateEntity();
                dirty.AddComponent(new TerrainPatchDirtyComponent
                {
                    MinX = 0,
                    MinZ = 0,
                    MaxX = 0,
                    MaxZ = 0
                });
            }
        }
    }
}
