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

            foreach (var entity in _entities.GetEntitiesWith<DigInteractionComponent>())
            {
                if (!entity.TryGetComponent<MiningHitComponent>(out var mining))
                    mining = new MiningHitComponent();

                var dig = entity.GetComponent<DigInteractionComponent>();
                if (!dig.IsDigHeld || !dig.HasGroundHit)
                {
                    mining.HasHit = false;
                    entity.SetComponent(mining);
                    continue;
                }

                var samplePos = dig.GroundHit;
                float depthProbe = 0.10f;
                samplePos.Y -= depthProbe;
                var block = editable.TryMineAt(samplePos, 0f, out var target)
                    ? target
                    : ResourceBlockType.None;

                mining.HasHit = block != ResourceBlockType.None;
                mining.HitPosition = dig.GroundHit;
                mining.BlockType = block;
                entity.SetComponent(mining);
            }
        }
    }
}
