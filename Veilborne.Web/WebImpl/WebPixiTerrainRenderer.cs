using System.Numerics;
using Microsoft.JSInterop;
using Veilborne;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.TerrainTexture;

namespace Veilborne.Web.WebImpl;

/// <summary>
/// Projects terrain to screen-space triangles. Texture detail comes from JS tile
/// patterns — C# only sends texture index + UV and screen coordinates.
/// </summary>
public sealed class WebPixiTerrainRenderer : ITerrainRenderer
{
    private const int GridStep = 10;
    private const int MaxTriangles = 1200;
    private const int IntsPerTriangle = 9; // texIdx, u, v, x1,y1,x2,y2,x3,y3

    private static readonly Dictionary<string, int> TextureIndex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brown_mud_leaves"] = 0,
        ["aerial_rocks"] = 1,
        ["lichen_rock"] = 2,
        ["brown_mud"] = 3,
        ["rock_3"] = 4,
        ["snow"] = 5,
    };

    private readonly IJSInProcessRuntime _js;
    private readonly IGraphicsProvider _graphics;
    private readonly ITerrainTextureRegistry _textureRegistry;
    private readonly List<PendingChunk> _pending = new(8);
    private readonly List<int> _triangleBuffer = new(MaxTriangles * IntsPerTriangle);
    private readonly Dictionary<ChunkColorKey, GridSampleCache> _gridCache = new();
    private int[] _jsBuffer = Array.Empty<int>();
    private bool _texturesInitialized;

    private string _primaryTexId = "brown_mud_leaves";
    private string _secondaryTexId = "";
    private float _secondaryTexBlend;

    private Vector3 _camPos;
    private Vector3 _camRight;
    private Vector3 _camUp;
    private Vector3 _camForward;
    private float _projScaleX;
    private float _projScaleY;
    private float _screenW;
    private float _screenH;
    private bool _cameraReady;

    public WebPixiTerrainRenderer(IJSRuntime js, IGraphicsProvider graphics, ITerrainTextureRegistry textureRegistry)
    {
        _js = (IJSInProcessRuntime)js;
        _graphics = graphics;
        _textureRegistry = textureRegistry;
    }

    public void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor) =>
        RenderAt(heights, tileSize, Vector2.Zero, camera);

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera) =>
        RenderAt(heights, tileSize, originWorld, camera, null, null);

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig) =>
        RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig, null);

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap)
    {
        if (heights == null || heights.Length == 0)
            return;

        EnsureTerrainTextures();
        _pending.Add(new PendingChunk(heights, tileSize, originWorld));
        EnsureCamera(camera);
    }

    public void ApplyBiomeTextures(BiomeData biome)
    {
        if (biome == null) return;
        _primaryTexId = ResolveTextureId(biome);
        _secondaryTexId = "";
        _secondaryTexBlend = 0f;
    }

    public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend)
    {
        if (primary == null) return;
        _primaryTexId = ResolveTextureId(primary);
        _secondaryTexBlend = Math.Clamp(secondaryBlend, 0f, 1f);
        if (secondary != null && _secondaryTexBlend > 0.01f)
            _secondaryTexId = ResolveTextureId(secondary);
        else
        {
            _secondaryTexId = "";
            _secondaryTexBlend = 0f;
        }
    }

    public void SetColorTint(Vector4 color) { }
    public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld) { }
    public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld) { }
    public void ProcessBuildQueue(int maxPerFrame) { }
    public void MarkOriginDirty(Vector2 originWorld) => RemoveCacheForOrigin(originWorld.X, originWorld.Y);
    public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1) =>
        RemoveCacheForOrigin(originWorld.X, originWorld.Y);

    public void Flush()
    {
        if (_pending.Count == 0)
            return;

        _triangleBuffer.Clear();
        int primaryIdx = ResolveTextureIndex(_primaryTexId);
        int secondaryIdx = ResolveTextureIndex(_secondaryTexId);

        foreach (var chunk in _pending)
        {
            if (_triangleBuffer.Count / IntsPerTriangle >= MaxTriangles)
                break;
            BuildTriangles(chunk, primaryIdx, secondaryIdx);
        }

        int triCount = _triangleBuffer.Count / IntsPerTriangle;
        if (triCount > 0)
        {
            if (_jsBuffer.Length < _triangleBuffer.Count)
                _jsBuffer = new int[_triangleBuffer.Count];
            _triangleBuffer.CopyTo(_jsBuffer, 0);
            _js.InvokeVoid("veilborne.pixi.drawTerrainBatchFlat8", _jsBuffer, triCount);
        }

        _pending.Clear();
        _cameraReady = false;

        if (_gridCache.Count > 64)
            _gridCache.Clear();
    }

    private void EnsureTerrainTextures()
    {
        if (_texturesInitialized) return;
        _js.InvokeVoid("veilborne.initTerrainTextures");
        _texturesInitialized = true;
    }

    private void EnsureCamera(CameraComponent camera)
    {
        if (_cameraReady) return;
        _camForward = Vector3.Normalize(camera.Target - camera.Position);
        _camRight = Vector3.Normalize(Vector3.Cross(_camForward, camera.Up));
        _camUp = Vector3.Normalize(Vector3.Cross(_camRight, _camForward));
        _camPos = camera.Position;

        float aspect = Math.Max(1f, _graphics.ScreenWidth) / Math.Max(1f, _graphics.ScreenHeight);
        float tanHalf = MathF.Tan(camera.FovY * (MathF.PI / 180f) * 0.5f);
        if (tanHalf <= 1e-5f) tanHalf = 0.4f;

        _projScaleX = 1f / (tanHalf * aspect);
        _projScaleY = 1f / tanHalf;
        _screenW = _graphics.ScreenWidth;
        _screenH = _graphics.ScreenHeight;
        _cameraReady = true;
    }

    private void BuildTriangles(PendingChunk chunk, int primaryIdx, int secondaryIdx)
    {
        var heights = chunk.Heights;
        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        if (width < 2 || depth < 2)
            return;

        int gridW = (width - 1) / GridStep + 1;
        int gridH = (depth - 1) / GridStep + 1;
        int gridCount = gridW * gridH;

        var cacheKey = new ChunkColorKey(chunk.Origin.X, chunk.Origin.Y, _primaryTexId, _secondaryTexId, _secondaryTexBlend);
        if (!_gridCache.TryGetValue(cacheKey, out var samples))
        {
            samples = BuildGridSamples(chunk, gridW, gridH, primaryIdx, secondaryIdx);
            _gridCache[cacheKey] = samples;
        }

        Span<int> px = gridCount <= 128 ? stackalloc int[gridCount] : new int[gridCount];
        Span<int> py = gridCount <= 128 ? stackalloc int[gridCount] : new int[gridCount];
        Span<bool> ok = gridCount <= 128 ? stackalloc bool[gridCount] : new bool[gridCount];

        for (int gz = 0; gz < gridH; gz++)
        {
            int iz = Math.Min(gz * GridStep, depth - 1);
            for (int gx = 0; gx < gridW; gx++)
            {
                int ix = Math.Min(gx * GridStep, width - 1);
                int idx = gz * gridW + gx;
                float wx = chunk.Origin.X + ix * chunk.TileSize;
                float wz = chunk.Origin.Y + iz * chunk.TileSize;
                float wy = heights[ix, iz];
                if (TryProject(wx, wy, wz, out int sx, out int sy))
                {
                    px[idx] = sx;
                    py[idx] = sy;
                    ok[idx] = true;
                }
            }
        }

        for (int gz = 0; gz < gridH - 1; gz++)
        {
            for (int gx = 0; gx < gridW - 1; gx++)
            {
                if (_triangleBuffer.Count / IntsPerTriangle >= MaxTriangles)
                    return;

                int i00 = gz * gridW + gx;
                int i10 = gz * gridW + gx + 1;
                int i01 = (gz + 1) * gridW + gx;
                int i11 = (gz + 1) * gridW + gx + 1;

                if (!ok[i00] && !ok[i10] && !ok[i01] && !ok[i11])
                    continue;

                int texIdx = samples.TexIdx[i00];
                int u = samples.U[i00];
                int v = samples.V[i00];

                if (ok[i00] && ok[i10] && ok[i01])
                    AddTriangle(texIdx, u, v, px[i00], py[i00], px[i10], py[i10], px[i01], py[i01]);
                if (ok[i10] && ok[i11] && ok[i01])
                    AddTriangle(texIdx, u, v, px[i10], py[i10], px[i11], py[i11], px[i01], py[i01]);
            }
        }
    }

    private GridSampleCache BuildGridSamples(PendingChunk chunk, int gridW, int gridH, int primaryIdx, int secondaryIdx)
    {
        int count = gridW * gridH;
        var tex = new int[count];
        var u = new int[count];
        var v = new int[count];
        var heights = chunk.Heights;
        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);

        for (int gz = 0; gz < gridH; gz++)
        {
            int iz = Math.Min(gz * GridStep, depth - 1);
            for (int gx = 0; gx < gridW; gx++)
            {
                int ix = Math.Min(gx * GridStep, width - 1);
                int idx = gz * gridW + gx;
                float wx = chunk.Origin.X + ix * chunk.TileSize;
                float wz = chunk.Origin.Y + iz * chunk.TileSize;
                float tileWorld = _textureRegistry.GetTileSizeOrDefault(_primaryTexId, 6f);
                float uvScale = 256f / Math.Max(tileWorld, 0.5f);

                int pu = (int)(MathF.Abs(wx * uvScale) % 256f);
                int pv = (int)(MathF.Abs(wz * uvScale) % 256f);
                u[idx] = pu;
                v[idx] = pv;
                tex[idx] = primaryIdx;

                if (_secondaryTexBlend > 0.01f && secondaryIdx >= 0)
                    tex[idx] = _secondaryTexBlend > 0.5f ? secondaryIdx : primaryIdx;
            }
        }

        return new GridSampleCache(tex, u, v);
    }

    private void AddTriangle(int texIdx, int u, int v, int x1, int y1, int x2, int y2, int x3, int y3)
    {
        _triangleBuffer.Add(texIdx);
        _triangleBuffer.Add(u);
        _triangleBuffer.Add(v);
        _triangleBuffer.Add(x1);
        _triangleBuffer.Add(y1);
        _triangleBuffer.Add(x2);
        _triangleBuffer.Add(y2);
        _triangleBuffer.Add(x3);
        _triangleBuffer.Add(y3);
    }

    private static string ResolveTextureId(BiomeData biome)
    {
        var layers = biome.SurfaceTextures;
        if (layers is { Count: > 0 } && !string.IsNullOrWhiteSpace(layers[0].TextureId))
            return layers[0].TextureId;
        return "brown_mud_leaves";
    }

    private static int ResolveTextureIndex(string id) =>
        TextureIndex.TryGetValue(id, out int idx) ? idx : 0;

    private void RemoveCacheForOrigin(float originX, float originZ)
    {
        var keys = _gridCache.Keys.Where(k => MathF.Abs(k.OriginX - originX) < 0.01f && MathF.Abs(k.OriginZ - originZ) < 0.01f).ToList();
        foreach (var key in keys)
            _gridCache.Remove(key);
    }

    private bool TryProject(float wx, float wy, float wz, out int sx, out int sy)
    {
        float rx = wx - _camPos.X;
        float ry = wy - _camPos.Y;
        float rz = wz - _camPos.Z;

        float xView = rx * _camRight.X + ry * _camRight.Y + rz * _camRight.Z;
        float yView = rx * _camUp.X + ry * _camUp.Y + rz * _camUp.Z;
        float zView = rx * _camForward.X + ry * _camForward.Y + rz * _camForward.Z;

        if (zView <= 0.5f)
        {
            sx = sy = 0;
            return false;
        }

        float invZ = 1f / zView;
        float xNdc = xView * invZ * _projScaleX;
        float yNdc = yView * invZ * _projScaleY;
        if (xNdc < -1.5f || xNdc > 1.5f || yNdc < -1.5f || yNdc > 1.5f)
        {
            sx = sy = 0;
            return false;
        }

        sx = (int)((xNdc * 0.5f + 0.5f) * _screenW);
        sy = (int)((1f - (yNdc * 0.5f + 0.5f)) * _screenH);
        return true;
    }

    private readonly record struct PendingChunk(float[,] Heights, float TileSize, Vector2 Origin);

    private readonly record struct ChunkColorKey(float OriginX, float OriginZ, string PrimaryTex, string SecondaryTex, float Blend);

    private sealed class GridSampleCache
    {
        public GridSampleCache(int[] texIdx, int[] u, int[] v)
        {
            TexIdx = texIdx;
            U = u;
            V = v;
        }

        public int[] TexIdx { get; }
        public int[] U { get; }
        public int[] V { get; }
    }
}
