using System.Numerics;
using Veilborne.Camera;
using Veilborne.Core.Tests.Fakes;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Unit;

public class InputSystemTests
{
    [Fact]
    public void Update_clears_horizontal_displacement()
    {
        var registry = new EntityRegistry();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new MoveInputComponent { HorizontalDisplacement = new Vector3(1, 0, 2) });

        new InputSystem(registry).Update(0.016f);

        var move = player.GetComponent<MoveInputComponent>();
        Assert.Equal(Vector3.Zero, move.HorizontalDisplacement);
    }
}

public class PlayerInputSystemTests
{
    [Fact]
    public void Update_forward_key_sets_positive_displacement_along_camera_forward()
    {
        var registry = new EntityRegistry();
        var input = new FakeInputProvider();
        var settings = new FakeGameSettingsService();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 2, 0));
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, 2, 10);
        player.SetComponent(cam);

        input.HoldKey(InputKeys.KEY_W);
        new PlayerInputSystem(registry, input, settings).Update(0.1f);

        var move = player.GetComponent<MoveInputComponent>();
        Assert.True(move.HorizontalDisplacement.Z > 0f);
        Assert.Equal(0f, move.HorizontalDisplacement.Y);
    }

    [Fact]
    public void Update_jump_press_sets_jump_buffer_timer()
    {
        var registry = new EntityRegistry();
        var input = new FakeInputProvider();
        var settings = new FakeGameSettingsService();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);

        input.PressKey(InputKeys.KEY_SPACE);
        new PlayerInputSystem(registry, input, settings).Update(0.016f);

        var jump = player.GetComponent<JumpComponent>();
        Assert.Equal(jump.JumpBufferSeconds, jump.JumpBufferTimer);
    }
}

public class SimplePhysicsControllerTests
{
    [Fact]
    public void Integrate_applies_gravity_and_snaps_to_ground()
    {
        var physics = new SimplePhysicsController(eyeHeight: 1.7f);
        var cam = new CameraComponent
        {
            Position = new Vector3(0, 5, 0),
            Target = new Vector3(0, 5, 1),
            Up = Vector3.UnitY
        };
        var jump = new JumpComponent();
        var vertical = new VerticalVelocityComponent();

        physics.Integrate(ref cam, ref jump, ref vertical, 0.5f, Vector3.Zero, -20f,
            (_, _) => 0f);

        Assert.True(jump.IsGrounded);
        Assert.Equal(1.7f, cam.Position.Y, precision: 2);
    }

    [Fact]
    public void Integrate_executes_buffered_jump_when_grounded()
    {
        var physics = new SimplePhysicsController(eyeHeight: 1.7f);
        var cam = new CameraComponent
        {
            Position = new Vector3(0, 1.7f, 0),
            Target = new Vector3(0, 1.7f, 1),
            Up = Vector3.UnitY
        };
        var jump = new JumpComponent
        {
            JumpSpeed = 8.5f,
            JumpBufferTimer = 0.1f,
            CoyoteTimer = 0.1f,
            IsGrounded = true
        };
        var vertical = new VerticalVelocityComponent();

        physics.Integrate(ref cam, ref jump, ref vertical, 0.016f, Vector3.Zero, -20f,
            (_, _) => 0f);

        Assert.True(vertical.Value > 7.5f);
        Assert.False(jump.IsGrounded);
    }
}

public class IntegrationSystemTests
{
    [Fact]
    public void Update_syncs_transform_to_camera_position()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 2, 0));
        player.SetComponent(new MoveInputComponent { HorizontalDisplacement = new Vector3(1, 0, 0) });

        new IntegrationSystem(registry, new SimplePhysicsController(), terrain).Update(0.1f);

        var transform = player.GetComponent<TransformComponent>();
        var cam = player.GetComponent<CameraComponent>();
        Assert.Equal(cam.Position, transform.Position);
    }
}

public class DigInputSystemTests
{
    [Fact]
    public void Update_sets_dig_held_when_mouse_button_down()
    {
        var registry = new EntityRegistry();
        var input = new FakeInputProvider();
        var settings = new FakeGameSettingsService();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);

        input.HoldMouseButton(InputKeys.MOUSE_BUTTON_LEFT);
        new DigInputSystem(registry, input, settings).Update(0.016f);

        Assert.True(player.GetComponent<DigInteractionComponent>().IsDigHeld);
    }
}

public class DigProbeSystemTests
{
    [Fact]
    public void Update_finds_ground_hit_when_looking_downward()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain { GroundHeight = 0f };
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 5, 0));
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, -5, 0);
        player.SetComponent(cam);

        player.SetComponent(new DigInteractionComponent
        {
            IsDigHeld = true,
            HasGroundHit = false,
            ProbeMaxDistance = 6f,
            ProbeStep = 0.25f,
            ProbeEpsilon = 0.05f
        });

        new DigProbeSystem(registry, terrain).Update(0.016f);

        var dig = player.GetComponent<DigInteractionComponent>();
        Assert.True(dig.HasGroundHit);
        Assert.Equal(0f, dig.GroundHit.Y, precision: 2);
    }

    [Fact]
    public void Update_skips_hit_when_look_angle_is_too_shallow()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain { GroundHeight = 0f };
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 5, 0));
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, 5, 10);
        player.SetComponent(cam);

        new DigProbeSystem(registry, terrain).Update(0.016f);

        Assert.False(player.GetComponent<DigInteractionComponent>().HasGroundHit);
    }
}

public class VoxelRaycastSystemTests
{
    [Fact]
    public void Update_populates_mining_hit_when_digging()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain { ProbeBlockType = ResourceBlockType.Dirt };
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent
        {
            IsDigHeld = true,
            HasGroundHit = true,
            GroundHit = new Vector3(1, 0, 2)
        });

        new VoxelRaycastSystem(registry, terrain).Update(0.016f);

        var mining = player.GetComponent<MiningHitComponent>();
        Assert.True(mining.HasHit);
        Assert.Equal(ResourceBlockType.Dirt, mining.BlockType);
    }
}

public class DepleteSystemTests
{
    [Fact]
    public void Update_spawns_item_drop_with_lifetime_when_block_depleted()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain { MinedBlockType = ResourceBlockType.Rock };
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent { IsDigHeld = true });
        player.SetComponent(new MiningHitComponent
        {
            HasHit = true,
            HitPosition = new Vector3(2, 0, 3)
        });

        new DepleteSystem(registry, terrain).Update(0.5f);

        Assert.Single(terrain.MineCalls);
        Assert.Equal(1, EcsTestFactory.CountEntitiesWith<ItemDropComponent>(registry));
        Assert.Equal(1, EcsTestFactory.CountEntitiesWith<ItemDropComponent, LifetimeComponent>(registry));
    }

    [Fact]
    public void Update_does_nothing_when_terrain_is_not_editable()
    {
        var registry = new EntityRegistry();
        var terrain = new NonEditableTerrain();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent { IsDigHeld = true });
        player.SetComponent(new MiningHitComponent { HasHit = true, HitPosition = Vector3.Zero });

        new DepleteSystem(registry, terrain).Update(0.5f);

        Assert.Equal(0, EcsTestFactory.CountEntitiesWith<ItemDropComponent>(registry));
    }

    private sealed class NonEditableTerrain : IInfiniteTerrain
    {
        public float SampleHeight(Vector3 worldPos) => 0f;
        public void UpdateCenter(Vector3 cameraPosition) { }
        public void Update() { }
        public void Render(CameraComponent camera) { }
        public void RenderWithExclusions(CameraComponent camera, HashSet<(int cx, int cz)> exclude) { }
        public void RenderDebugChunkBounds(CameraComponent camera) { }
        public TerrainDebugInfo GetDebugInfo(Vector3 worldPos) => default;
    }
}

public class DigExecutionSystemTests
{
    [Fact]
    public void Update_queues_dig_sphere_when_dig_held_and_ground_hit()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain();
        var config = new FakeWorldConfigService();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent
        {
            IsDigHeld = true,
            HasGroundHit = true,
            GroundHit = new Vector3(0, 0, 0)
        });

        new DigExecutionSystem(registry, terrain, config, new SeededRandomSource()).Update(0.1f);

        Assert.Single(terrain.DigCalls);
    }

    [Fact]
    public void Update_spawns_particles_with_seeded_random()
    {
        var registry = new EntityRegistry();
        var terrain = new FakeEditableTerrain();
        var config = new FakeWorldConfigService
        {
            Config = new WorldConfig
            {
                Dig = new DigConfig { SpawnParticles = true, ParticlesPerDig = 2, ParticleLifetime = 1f }
            }
        };
        var random = new SeededRandomSource();
        random.Enqueue(0.0, 0.5, 0.5, 0.0, 0.5, 0.5);
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent
        {
            IsDigHeld = true,
            HasGroundHit = true,
            GroundHit = new Vector3(0, 0, 0)
        });

        new DigExecutionSystem(registry, terrain, config, random).Update(0.1f);

        Assert.Equal(2, EcsTestFactory.CountEntitiesWith<DigParticleComponent>(registry));
    }
}

public class CleanupSystemTests
{
    [Fact]
    public void Update_destroys_entities_with_expired_lifetime()
    {
        var registry = new EntityRegistry();
        var entity = registry.CreateEntity();
        entity.AddComponent(new LifetimeComponent { RemainingSeconds = 0.01f });

        new CleanupSystem(registry).Update(0.02f);

        Assert.Equal(0, EcsTestFactory.CountEntitiesWith<LifetimeComponent>(registry));
    }

    [Fact]
    public void Update_preserves_entities_with_zero_lifetime_as_immortal()
    {
        var registry = new EntityRegistry();
        var entity = registry.CreateEntity();
        entity.AddComponent(new LifetimeComponent { RemainingSeconds = 0f });

        new CleanupSystem(registry).Update(1f);

        Assert.Equal(1, EcsTestFactory.CountEntitiesWith<LifetimeComponent>(registry));
    }
}

public class DependencySystemTests
{
    [Fact]
    public void Update_rebuilds_children_from_parent_links()
    {
        var registry = new EntityRegistry();
        var parent = registry.CreateEntity();
        parent.AddComponent(new ChildrenComponent { EntityIds = [] });
        var child = registry.CreateEntity();
        child.AddComponent(new ParentComponent { EntityId = parent.Id });

        new DependencySystem(registry).Update(0.016f);

        var children = parent.GetComponent<ChildrenComponent>();
        Assert.Contains(child.Id, children.EntityIds);
    }
}

public class CollisionSystemsTests
{
    [Fact]
    public void Detection_and_resolution_push_player_out_of_world_object()
    {
        var registry = new EntityRegistry();
        var buffer = new CollisionFrameBuffer();
        var spatialIndex = new WorldObjectSpatialIndex();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(1f, 2f, 0f));
        player.SetComponent(new VelocityComponent { Linear = Vector3.Zero });
        EcsTestFactory.CreateWorldObject(registry, Vector3.Zero, radius: 1.5f);

        new WorldObjectSpatialIndexSystem(registry, spatialIndex).Update(0.016f);
        new CollisionDetectionSystem(registry, buffer, spatialIndex).Update(0.016f);
        var startPos = player.GetComponent<CameraComponent>().Position;
        new CollisionResolutionSystem(registry, buffer).Update(0.016f);
        var endPos = player.GetComponent<CameraComponent>().Position;

        Assert.True(Vector3.Distance(endPos, startPos) > 0f);
        Assert.True(endPos.X > startPos.X);
    }
}

public class BiomeAssetSystemsTests
{
    [Fact]
    public void Discovery_creates_load_request_for_active_biome()
    {
        var registry = new EntityRegistry();
        var tracker = new BiomeAssetTracker();
        EcsTestFactory.CreateBiomeChunk(registry, "forest");

        var system = new BiomeDiscoverySystem(registry, tracker);
        system.Update(0.016f);
        system.Update(0.016f);
        system.Update(0.016f);

        Assert.Equal(1, EcsTestFactory.CountEntitiesWith<BiomeLoadRequestComponent>(registry));
        Assert.Contains("forest", tracker.Requested);
    }

    [Fact]
    public void Asset_load_marks_biome_loaded_and_removes_request()
    {
        var registry = new EntityRegistry();
        var tracker = new BiomeAssetTracker();
        tracker.Requested.Add("forest");
        var request = registry.CreateEntity();
        request.AddComponent(new BiomeLoadRequestComponent { BiomeId = "forest" });

        new AssetLoadSystem(registry, tracker).Update(0.016f);

        Assert.Contains("forest", tracker.Loaded);
        Assert.DoesNotContain("forest", tracker.Requested);
        Assert.Equal(0, EcsTestFactory.CountEntitiesWith<BiomeLoadRequestComponent>(registry));
    }

    [Fact]
    public void Asset_unload_removes_bundle_when_refs_are_zero()
    {
        var registry = new EntityRegistry();
        var tracker = new BiomeAssetTracker();
        tracker.Loaded.Add("forest");
        EcsTestFactory.CreateLoadedBiomeBundle(registry, "forest");

        new AssetUnloadSystem(registry, tracker).Update(0.016f);

        Assert.DoesNotContain("forest", tracker.Loaded);
        Assert.Equal(0, EcsTestFactory.CountEntitiesWith<BiomeLoadedAssetsComponent>(registry));
    }
}

public class FrustumCullAndSortTests
{
    [Fact]
    public void FrustumCullSystem_hides_entities_beyond_distance()
    {
        var registry = new EntityRegistry();
        var frame = new EcsFrameContext();
        frame.SetPrimaryCamera(Vector3.Zero);
        var near = EcsTestFactory.CreateWorldObject(registry, new Vector3(5, 0, 0), 1f);
        var far = EcsTestFactory.CreateWorldObject(registry, new Vector3(500, 0, 0), 1f);

        new FrustumCullSystem(registry, frame).Update(0.016f);

        Assert.True(near.GetComponent<RenderComponent>().Visible);
        Assert.False(far.GetComponent<RenderComponent>().Visible);
    }

    [Fact]
    public void SortSystem_marks_frame_sorted_when_visible_entities_exist()
    {
        var registry = new EntityRegistry();
        var frame = new EcsFrameContext();
        frame.SetPrimaryCamera(Vector3.Zero);
        EcsTestFactory.CreateWorldObject(registry, new Vector3(5, 0, 0), 1f);

        new FrustumCullSystem(registry, frame).Update(0.016f);
        new SortSystem(registry, frame).Update(0.016f);

        Assert.True(frame.WasSortedThisFrame);
    }
}

public class UISystemTests
{
    [Fact]
    public void Update_sets_crosshair_hit_text_when_ground_hit()
    {
        var registry = new EntityRegistry();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        player.SetComponent(new DigInteractionComponent { HasGroundHit = true });
        var hud = UiEntityFactory.CreateHudUi(registry, player.Id);

        new UISystem(registry).Update(0.016f);

        Assert.Equal("hit", hud.Crosshair.GetComponent<UIElementComponent>().Text);
    }
}

public class PlayerEntityFactoryTests
{
    [Fact]
    public void CreateDefault_adds_all_required_gameplay_components()
    {
        var registry = new EntityRegistry();
        var player = PlayerEntityFactory.CreateDefault(registry, new Vector3(1, 2, 3));

        Assert.True(player.HasComponent<PlayerComponent>());
        Assert.True(player.HasComponent<CameraComponent>());
        Assert.True(player.HasComponent<DigInteractionComponent>());
        Assert.True(player.HasComponent<MoveInputComponent>());
        Assert.True(player.HasComponent<ColliderComponent>());
        Assert.Equal(new Vector3(1, 2, 3), player.GetComponent<TransformComponent>().Position);
    }
}

public class EcsSystemPipelineTests
{
    [Fact]
    public void UpdateSystemTypes_contains_core_gameplay_systems_in_order()
    {
        var types = EcsSystemPipeline.UpdateSystemTypes.ToList();

        Assert.Equal(typeof(CleanupSystem), types[0]);
        Assert.True(types.IndexOf(typeof(PlayerInputSystem)) > types.IndexOf(typeof(InputSystem)));
        Assert.True(types.IndexOf(typeof(CollisionResolutionSystem)) > types.IndexOf(typeof(CollisionDetectionSystem)));
        Assert.Equal(typeof(UISystem), types[^1]);
    }
}
