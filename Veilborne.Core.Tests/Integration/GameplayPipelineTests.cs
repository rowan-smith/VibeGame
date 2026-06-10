using System.Numerics;
using Veilborne.Camera;
using Veilborne.Core.Tests.Fakes;
using Veilborne.Core.Tests.Helpers;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Integration;

public class GameplayPipelineTests
{
    [Fact]
    public void Input_to_integration_moves_player_forward()
    {
        var registry = new EntityRegistry();
        var input = new FakeInputProvider();
        var settings = new FakeGameSettingsService();
        var terrain = new FakeEditableTerrain();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 1.7f, 0));
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, 1.7f, 10);
        player.SetComponent(cam);

        input.HoldKey(InputKeys.KEY_W);
        var runner = new SystemPipelineRunner(
            new InputSystem(registry),
            new CameraSystem(registry, new EcsFrameContext()),
            new PlayerInputSystem(registry, input, settings),
            new IntegrationSystem(registry, new SimplePhysicsController(), terrain));

        runner.Update(0.1f);

        var endPos = player.GetComponent<CameraComponent>().Position;
        Assert.True(endPos.Z > 0f);
    }

    [Fact]
    public void Dig_pipeline_sets_crosshair_hit_and_queues_mining()
    {
        var registry = new EntityRegistry();
        var input = new FakeInputProvider();
        var settings = new FakeGameSettingsService();
        var terrain = new FakeEditableTerrain { ProbeBlockType = ResourceBlockType.Dirt };
        var config = new FakeWorldConfigService();
        var frame = new EcsFrameContext();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(0, 5, 0));
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, -5, 0);
        player.SetComponent(cam);
        var hud = UiEntityFactory.CreateHudUi(registry, player.Id);

        input.HoldMouseButton(InputKeys.MOUSE_BUTTON_LEFT);
        var runner = new SystemPipelineRunner(
            new DigInputSystem(registry, input, settings),
            new DigProbeSystem(registry, terrain),
            new DigProbeSystem(registry, terrain),
            new VoxelRaycastSystem(registry, terrain),
            new DigExecutionSystem(registry, terrain, config, new SeededRandomSource()),
            new UISystem(registry));

        runner.Update(0.1f);

        Assert.True(player.GetComponent<DigInteractionComponent>().IsDigHeld);
        Assert.True(player.GetComponent<DigInteractionComponent>().HasGroundHit);
        Assert.Equal("hit", hud.Crosshair.GetComponent<UIElementComponent>().Text);
        Assert.NotEmpty(terrain.DigCalls);
    }

    [Fact]
    public void Terrain_streaming_system_calls_update_around_when_camera_moves()
    {
        var registry = new EntityRegistry();
        var streaming = new FakeTerrainStreaming();
        var config = new FakeWorldConfigService();
        EcsTestFactory.CreateMinimalPlayer(registry, new Vector3(10, 2, 10));

        var system = new TerrainLoadQueueSystem(registry, streaming, config);
        system.Update(0.2f);

        Assert.Equal(1, streaming.UpdateAroundCallCount);
        Assert.Equal(new Vector3(10, 2, 10), streaming.LastUpdatePosition);
    }

    [Fact]
    public void Biome_discovery_to_unload_lifecycle()
    {
        var registry = new EntityRegistry();
        var tracker = new BiomeAssetTracker();
        EcsTestFactory.CreateBiomeChunk(registry, "plains");

        var discovery = new BiomeDiscoverySystem(registry, tracker);
        discovery.Update(0.016f);
        discovery.Update(0.016f);
        discovery.Update(0.016f);
        new AssetLoadSystem(registry, tracker).Update(0.016f);

        Assert.Equal(1, EcsTestFactory.CountEntitiesWith<BiomeLoadedAssetsComponent>(registry));
        Assert.Contains("plains", tracker.Loaded);

        tracker.ActiveChunkRefs.Clear();
        new AssetUnloadSystem(registry, tracker).Update(0.016f);

        Assert.Equal(0, EcsTestFactory.CountEntitiesWith<BiomeLoadedAssetsComponent>(registry));
        Assert.DoesNotContain("plains", tracker.Loaded);
    }
}
