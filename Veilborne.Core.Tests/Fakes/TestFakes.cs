using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Fakes;

public sealed class FakeInputProvider : IInputProvider
{
    private readonly HashSet<int> _downKeys = new();
    private readonly HashSet<int> _pressedKeys = new();
    private readonly HashSet<int> _downMouseButtons = new();
    private readonly HashSet<int> _pressedMouseButtons = new();
    private readonly HashSet<int> _releasedMouseButtons = new();

    public Vector2 MousePosition { get; set; }
    public Vector2 MouseDelta { get; set; }
    public float MouseWheelMove { get; set; }

    public void UpdateStates()
    {
        _pressedKeys.Clear();
        _pressedMouseButtons.Clear();
        _releasedMouseButtons.Clear();
    }

    public void HoldKey(int key) => _downKeys.Add(key);
    public void ReleaseKey(int key) => _downKeys.Remove(key);
    public void PressKey(int key)
    {
        _downKeys.Add(key);
        _pressedKeys.Add(key);
    }

    public void HoldMouseButton(int button) => _downMouseButtons.Add(button);
    public void PressMouseButton(int button)
    {
        _downMouseButtons.Add(button);
        _pressedMouseButtons.Add(button);
    }

    public Vector2 GetMousePosition() => MousePosition;
    public Vector2 GetMouseDelta() => MouseDelta;
    public float GetMouseWheelMove() => MouseWheelMove;
    public bool IsKeyDown(int key) => _downKeys.Contains(key);
    public bool IsKeyPressed(int key) => _pressedKeys.Contains(key);
    public bool IsMouseButtonDown(int button) => _downMouseButtons.Contains(button);
    public bool IsMouseButtonPressed(int button) => _pressedMouseButtons.Contains(button);
    public bool IsMouseButtonReleased(int button) => _releasedMouseButtons.Contains(button);
    public IReadOnlyList<int> GetPressedKeys() => _pressedKeys.ToList();
    public IReadOnlyList<int> GetPressedMouseButtons() => _pressedMouseButtons.ToList();
    public void ShowCursor() { }
    public void HideCursor() { }
}

public sealed class FakeEditableTerrain : IEditableTerrain
{
    public float GroundHeight { get; set; } = 0f;
    public ResourceBlockType ProbeBlockType { get; set; } = ResourceBlockType.Dirt;
    public bool MineSucceeds { get; set; } = true;
    public ResourceBlockType MinedBlockType { get; set; } = ResourceBlockType.Dirt;

    public List<(Vector3 Center, float Radius, float Strength, VoxelFalloff Falloff)> DigCalls { get; } = new();
    public List<(Vector3 Position, float Power)> MineCalls { get; } = new();

    public float SampleHeight(Vector3 worldPos) => GroundHeight;

    public void UpdateCenter(Vector3 cameraPosition) { }

    public void Update() { }

    public void Render(Ecs.Components.CameraComponent camera) { }

    public void RenderWithExclusions(Ecs.Components.CameraComponent camera, HashSet<(int cx, int cz)> exclude) { }

    public void RenderDebugChunkBounds(Ecs.Components.CameraComponent camera) { }

    public TerrainDebugInfo GetDebugInfo(Vector3 worldPos) => default;

    public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
    {
        DigCalls.Add((worldCenter, radius, strength, falloff));
        return Task.CompletedTask;
    }

    public Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff)
        => Task.CompletedTask;

    public bool TryMineAt(Vector3 position, float power, out ResourceBlockType blockType)
    {
        MineCalls.Add((position, power));
        if (power <= 0f)
        {
            blockType = ProbeBlockType;
            return ProbeBlockType != ResourceBlockType.None;
        }

        if (!MineSucceeds)
        {
            blockType = ResourceBlockType.None;
            return false;
        }

        blockType = MinedBlockType;
        return true;
    }
}

public sealed class FakeGameSettingsService : Settings.IGameSettingsService
{
    public Settings.GameSettings Current { get; private set; } = new();

    public void Save() { }

    public void Update(Action<Settings.GameSettings> update) => update(Current);
}

public sealed class FakeItemRegistry : IItemRegistry
{
    public IReadOnlyList<Items.ItemDef> All { get; } = Array.Empty<Items.ItemDef>();

    public bool TryGet(string id, out Items.ItemDef item)
    {
        item = null!;
        return false;
    }

    public Items.Item? GetItemInSlot(int slot) => null;
}

public sealed class FakeWorldConfigService : IWorldConfigService
{
    public int Seed { get; init; } = 42;
    public WorldConfig Config { get; init; } = new();
    public Terrain.TerrainRingConfig TerrainConfig { get; init; } = new();
    public Biomes.Environment.MultiNoiseConfig NoiseConfig { get; init; } = new();
    public Biomes.BiomeProviderConfig BiomeProviderConfig { get; init; } = new();
}

public sealed class FakeTerrainStreaming : ITerrainStreaming
{
    public bool WarmupMode { get; private set; }
    public int UpdateAroundCallCount { get; private set; }
    public Vector3 LastUpdatePosition { get; private set; }
    public int LastQueueRadius { get; private set; }
    public int PumpCallCount { get; private set; }
    public TerrainLoadingProgress LoadingProgress { get; set; } =
        new(1f, "Complete", 0, 0, 0, 0, 0);

    public void SetWarmupMode(bool enabled) => WarmupMode = enabled;

    public void UpdateAround(Vector3 worldPos, int queueRadiusHint)
    {
        UpdateAroundCallCount++;
        LastUpdatePosition = worldPos;
        LastQueueRadius = queueRadiusHint;
    }

    public Task PumpAsyncJobs()
    {
        PumpCallCount++;
        return Task.CompletedTask;
    }

    public void ProcessPendingMeshBuilds() { }

    public TerrainLoadingProgress GetLoadingProgress() => LoadingProgress;
}

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Queue<double> _values = new();

    public void Enqueue(params double[] values)
    {
        foreach (var value in values)
            _values.Enqueue(value);
    }

    public double NextDouble() => _values.Count > 0 ? _values.Dequeue() : 0.5;
}
