using System.Numerics;
using Veilborne;
using Veilborne.Core.Tests.Fakes;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Ecs.Systems;
using Veilborne.GameFlow;
using Veilborne.Interfaces;
using Veilborne.Stubs;
using Veilborne.Terrain;
using Veilborne.UI;

namespace Veilborne.Core.Tests.Unit;

public class GameFlowControllerTests
{
    [Fact]
    public void BeginLoading_enters_loading_state_and_starts_warmup()
    {
        var flow = new GameFlowController();
        var streaming = new FakeTerrainStreaming();

        flow.BeginLoading(streaming);

        Assert.Equal(GameFlowState.Loading, flow.State);
        Assert.True(streaming.WarmupMode);
    }

    [Fact]
    public void UpdateLoading_transitions_to_playing_when_progress_complete()
    {
        var flow = new GameFlowController();
        var streaming = new FakeTerrainStreaming
        {
            LoadingProgress = new TerrainLoadingProgress(1f, "Complete", 4, 4, 0, 10, 0)
        };
        flow.BeginLoading(streaming);

        bool enteredPlaying = false;
        for (int i = 0; i < 20 && !enteredPlaying; i++)
            enteredPlaying = flow.UpdateLoading(0.02f, streaming, Vector3.Zero);

        Assert.True(enteredPlaying);
        Assert.Equal(GameFlowState.Playing, flow.State);
        Assert.False(streaming.WarmupMode);
    }

    [Fact]
    public void CancelLoading_returns_to_main_menu_and_disables_warmup()
    {
        var flow = new GameFlowController();
        var streaming = new FakeTerrainStreaming();
        flow.BeginLoading(streaming);

        flow.CancelLoading(streaming);

        Assert.Equal(GameFlowState.MainMenu, flow.State);
        Assert.False(streaming.WarmupMode);
    }

    [Fact]
    public void ApplyMenuAction_resume_enters_playing_state()
    {
        var flow = new GameFlowController { };
        flow.ApplyMenuAction(MenuAction.Resume);

        Assert.Equal(GameFlowState.Playing, flow.State);
    }

    [Fact]
    public void LoadingSession_keeps_progress_monotonic()
    {
        var loading = new LoadingSessionController();
        var streaming = new FakeTerrainStreaming
        {
            LoadingProgress = new TerrainLoadingProgress(0.8f, "A", 4, 2, 1, 0, 0)
        };
        loading.BeginWarmup(streaming);
        loading.Update(0.016f, streaming, Vector3.Zero);

        streaming.LoadingProgress = new TerrainLoadingProgress(0.5f, "B", 4, 2, 1, 0, 0);
        loading.Update(0.016f, streaming, Vector3.Zero);

        Assert.Equal(0.8f, loading.Progress);
        Assert.Equal("B", loading.StageText);
    }
}

public class RenderSystemsTests
{
    [Fact]
    public void TerrainRenderSystem_renders_for_valid_camera()
    {
        var registry = new EntityRegistry();
        var terrain = new RecordingInfiniteTerrain();
        var renderer = new CountingTerrainRenderer();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = new Vector3(0, 0, 10);
        player.SetComponent(cam);

        new TerrainRenderSystem(registry, terrain, renderer).Draw();

        Assert.Equal(1, terrain.RenderCallCount);
        Assert.Equal(1, renderer.FlushCallCount);
    }

    [Fact]
    public void TerrainRenderSystem_skips_invalid_camera_vectors()
    {
        var registry = new EntityRegistry();
        var terrain = new RecordingInfiniteTerrain();
        var renderer = new CountingTerrainRenderer();
        var player = EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);
        var cam = player.GetComponent<CameraComponent>();
        cam.Target = cam.Position;
        player.SetComponent(cam);

        new TerrainRenderSystem(registry, terrain, renderer).Draw();

        Assert.Equal(0, terrain.RenderCallCount);
        Assert.Equal(1, renderer.FlushCallCount);
    }

    [Fact]
    public void ObjectRenderSystem_renders_when_camera_exists()
    {
        var registry = new EntityRegistry();
        var renderer = new RecordingWorldObjectRenderer();
        EcsTestFactory.CreateMinimalPlayer(registry, Vector3.Zero);

        new ObjectRenderSystem(registry, renderer).Draw();

        Assert.Equal(1, renderer.RenderCallCount);
    }

    [Fact]
    public void ObjectRenderSystem_skips_without_camera()
    {
        var registry = new EntityRegistry();
        var renderer = new RecordingWorldObjectRenderer();

        new ObjectRenderSystem(registry, renderer).Draw();

        Assert.Equal(0, renderer.RenderCallCount);
    }

    [Fact]
    public void RenderSystemTypes_lists_terrain_before_objects()
    {
        var types = EcsRenderSystemPipeline.RenderSystemTypes;
        Assert.Equal(typeof(TerrainRenderSystem), types[0]);
        Assert.Equal(typeof(ObjectRenderSystem), types[1]);
    }

    [Fact]
    public void Build_creates_render_systems_in_pipeline_order()
    {
        var registry = new EntityRegistry();
        var terrain = new RecordingInfiniteTerrain();
        var terrainRenderer = new CountingTerrainRenderer();
        var objectRenderer = new RecordingWorldObjectRenderer();

        var pipeline = EcsRenderSystemPipeline.Build(registry, terrain, terrainRenderer, objectRenderer);

        Assert.Equal(2, pipeline.Count);
        Assert.IsType<TerrainRenderSystem>(pipeline[0]);
        Assert.IsType<ObjectRenderSystem>(pipeline[1]);
    }

    private sealed class RecordingInfiniteTerrain : IInfiniteTerrain
    {
        public int RenderCallCount { get; private set; }

        public float SampleHeight(Vector3 worldPos) => 0f;
        public void UpdateCenter(Vector3 cameraPosition) { }
        public void Update() { }
        public void Render(CameraComponent camera) => RenderCallCount++;
        public void RenderWithExclusions(CameraComponent camera, HashSet<(int cx, int cz)> exclude) { }
        public void RenderDebugChunkBounds(CameraComponent camera) { }
        public TerrainDebugInfo GetDebugInfo(Vector3 worldPos) => default;
    }

    private sealed class CountingTerrainRenderer : ITerrainRenderer
    {
        private readonly StubTerrainRenderer _inner = new();
        public int FlushCallCount { get; private set; }

        public void ApplyBiomeTextures(Biomes.BiomeData biome) => _inner.ApplyBiomeTextures(biome);
        public void ApplyBiomeBlendTextures(Biomes.BiomeData primary, Biomes.BiomeData? secondary, float secondaryBlend)
            => _inner.ApplyBiomeBlendTextures(primary, secondary, secondaryBlend);
        public void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor)
            => _inner.Render(heights, tileSize, camera, baseColor);
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera)
            => _inner.RenderAt(heights, tileSize, originWorld, camera);
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig)
            => _inner.RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig);
        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap)
            => _inner.RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig, splatmap);
        public void SetColorTint(Vector4 color) => _inner.SetColorTint(color);
        public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld) => _inner.BuildChunks(heights, tileSize, originWorld);
        public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld) => _inner.EnqueueBuild(heights, tileSize, originWorld);
        public void ProcessBuildQueue(int maxPerFrame) => _inner.ProcessBuildQueue(maxPerFrame);
        public void MarkOriginDirty(Vector2 originWorld) => _inner.MarkOriginDirty(originWorld);
        public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1)
            => _inner.PatchRegion(heights, tileSize, originWorld, x0, z0, x1, z1);
        public void Flush() => FlushCallCount++;
    }

    private sealed class RecordingWorldObjectRenderer : Objects.IWorldObjectRenderer
    {
        public int RenderCallCount { get; private set; }
        public void Render(CameraComponent camera) => RenderCallCount++;
        public void DrawWorldObject(Objects.SpawnedObject obj) { }
    }
}
