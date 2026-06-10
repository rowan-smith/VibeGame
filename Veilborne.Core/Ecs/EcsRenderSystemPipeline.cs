using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.Objects;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Defines the canonical ECS render system order and factory helpers.
    /// </summary>
    public static class EcsRenderSystemPipeline
    {
        public static IReadOnlyList<Type> RenderSystemTypes { get; } =
        [
            typeof(TerrainRenderSystem),
            typeof(ObjectRenderSystem),
        ];

        public static IReadOnlyList<IRenderSystem> Build(
            EntityRegistry entities,
            IInfiniteTerrain terrain,
            ITerrainRenderer terrainRenderer,
            IWorldObjectRenderer worldObjectRenderer)
        {
            return
            [
                new TerrainRenderSystem(entities, terrain, terrainRenderer),
                new ObjectRenderSystem(entities, worldObjectRenderer),
            ];
        }
    }
}
