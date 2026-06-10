using Microsoft.Extensions.DependencyInjection;
using Veilborne.Ecs.Systems;

namespace Veilborne.Ecs
{
    /// <summary>
    /// Defines the canonical ECS update system order. Order is significant — do not reorder casually.
    /// </summary>
    public static class EcsSystemPipeline
    {
        public static IReadOnlyList<ISystem> BuildUpdatePipeline(IServiceProvider services)
        {
            return
            [
                services.GetRequiredService<CleanupSystem>(),
                services.GetRequiredService<DependencySystem>(),
                services.GetRequiredService<InputSystem>(),
                services.GetRequiredService<DigInputSystem>(),
                services.GetRequiredService<DigProbeSystem>(),
                services.GetRequiredService<VoxelRaycastSystem>(),
                services.GetRequiredService<CameraSystem>(),
                services.GetRequiredService<PlayerSystem>(),
                services.GetRequiredService<PlayerInputSystem>(),
                services.GetRequiredService<HotbarSelectionSystem>(),
                services.GetRequiredService<DepleteSystem>(),
                services.GetRequiredService<DigExecutionSystem>(),
                services.GetRequiredService<DigParticleSystem>(),
                services.GetRequiredService<AISystem>(),
                services.GetRequiredService<AnimationSystem>(),
                services.GetRequiredService<ParticleSystem>(),
                services.GetRequiredService<BiomeDiscoverySystem>(),
                services.GetRequiredService<AssetLoadSystem>(),
                services.GetRequiredService<BiomePrepSystem>(),
                services.GetRequiredService<ForceSystem>(),
                services.GetRequiredService<IntegrationSystem>(),
                services.GetRequiredService<WorldObjectSpatialIndexSystem>(),
                services.GetRequiredService<CollisionDetectionSystem>(),
                services.GetRequiredService<CollisionResolutionSystem>(),
                services.GetRequiredService<ConstraintSystem>(),
                services.GetRequiredService<TerrainLoadQueueSystem>(),
                services.GetRequiredService<TerrainLoadSystem>(),
                services.GetRequiredService<TerrainGenSystem>(),
                services.GetRequiredService<VegetationSystem>(),
                services.GetRequiredService<FrustumCullSystem>(),
                services.GetRequiredService<SortSystem>(),
                services.GetRequiredService<AssetUnloadSystem>(),
                services.GetRequiredService<ShadowMapSystem>(),
                services.GetRequiredService<EffectSystem>(),
                services.GetRequiredService<UISystem>(),
            ];
        }

        public static IReadOnlyList<Type> UpdateSystemTypes { get; } =
        [
            typeof(CleanupSystem),
            typeof(DependencySystem),
            typeof(InputSystem),
            typeof(DigInputSystem),
            typeof(DigProbeSystem),
            typeof(VoxelRaycastSystem),
            typeof(CameraSystem),
            typeof(PlayerSystem),
            typeof(PlayerInputSystem),
            typeof(HotbarSelectionSystem),
            typeof(DepleteSystem),
            typeof(DigExecutionSystem),
            typeof(DigParticleSystem),
            typeof(AISystem),
            typeof(AnimationSystem),
            typeof(ParticleSystem),
            typeof(BiomeDiscoverySystem),
            typeof(AssetLoadSystem),
            typeof(BiomePrepSystem),
            typeof(ForceSystem),
            typeof(IntegrationSystem),
            typeof(WorldObjectSpatialIndexSystem),
            typeof(CollisionDetectionSystem),
            typeof(CollisionResolutionSystem),
            typeof(ConstraintSystem),
            typeof(TerrainLoadQueueSystem),
            typeof(TerrainLoadSystem),
            typeof(TerrainGenSystem),
            typeof(VegetationSystem),
            typeof(FrustumCullSystem),
            typeof(SortSystem),
            typeof(AssetUnloadSystem),
            typeof(ShadowMapSystem),
            typeof(EffectSystem),
            typeof(UISystem),
        ];
    }
}
