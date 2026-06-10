using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Converts dig probe hits into mining-target hits.
    /// </summary>
    public class VoxelRaycastSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;

        public VoxelRaycastSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            if (_terrain is not IEditableTerrain editable)
                return;

            _entities.ForEachWith<DigInteractionComponent>((Entity entity, ref DigInteractionComponent dig) =>
            {
                if (!entity.TryGetComponent<MiningHitComponent>(out var mining))
                    mining = new MiningHitComponent();

                if (!dig.IsDigHeld || !dig.HasGroundHit)
                {
                    mining.HasHit = false;
                    entity.SetComponent(mining);
                    return;
                }

                var samplePos = dig.GroundHit;
                samplePos.Y -= 0.10f;
                var block = editable.TryMineAt(samplePos, 0f, out var target)
                    ? target
                    : ResourceBlockType.None;

                mining.HasHit = block != ResourceBlockType.None;
                mining.HitPosition = dig.GroundHit;
                mining.BlockType = block;
                entity.SetComponent(mining);
            });
        }
    }
}
