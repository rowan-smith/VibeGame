using System.Numerics;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Helpers;

public static class EcsTestFactory
{
    public static Entity CreateMinimalPlayer(EntityRegistry registry, Vector3 position)
        => PlayerEntityFactory.CreateDefault(registry, position);

    public static Entity CreateWorldObject(EntityRegistry registry, Vector3 position, float radius)
    {
        var entity = registry.CreateEntity();
        entity.AddComponent(new WorldObjectComponent());
        entity.AddComponent(new TransformComponent
        {
            Position = position,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        });
        entity.AddComponent(new ColliderComponent { Radius = radius });
        entity.AddComponent(new CollisionFilterComponent
        {
            Layer = CollisionLayer.WorldStatic,
            CollidesWith = CollisionLayer.Player
        });
        entity.AddComponent(new RenderComponent { Visible = true });
        return entity;
    }

    public static Entity CreateBiomeChunk(EntityRegistry registry, string biomeId)
    {
        var entity = registry.CreateEntity();
        entity.AddComponent(new TerrainChunkComponent { ChunkX = 0, ChunkZ = 0 });
        entity.AddComponent(new BiomeComponent { BiomeId = biomeId });
        return entity;
    }

    public static Entity CreateLoadedBiomeBundle(EntityRegistry registry, string biomeId)
    {
        var entity = registry.CreateEntity();
        entity.AddComponent(new BiomeLoadedAssetsComponent
        {
            BiomeId = biomeId,
            GrassTexturePath = $"assets/textures/{biomeId}_grass.png",
            TreeModelPath = "assets/models/tree_oak.glb"
        });
        return entity;
    }

    public static int CountEntitiesWith<T>(EntityRegistry registry) where T : struct, IComponent
    {
        int count = 0;
        registry.ForEachWith<T>(_ => count++);
        return count;
    }

    public static int CountEntitiesWith<T1, T2>(EntityRegistry registry)
        where T1 : struct, IComponent where T2 : struct, IComponent
    {
        int count = 0;
        registry.ForEachWith<T1, T2>(_ => count++);
        return count;
    }
}

public sealed class SystemPipelineRunner
{
    private readonly IReadOnlyList<ISystem> _systems;

    public SystemPipelineRunner(params ISystem[] systems) => _systems = systems;

    public SystemPipelineRunner(IEnumerable<ISystem> systems) => _systems = systems.ToList();

    public void Update(float dt)
    {
        foreach (var system in _systems)
            system.Update(dt);
    }
}
