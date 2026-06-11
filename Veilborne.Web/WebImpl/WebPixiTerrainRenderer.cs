using System.Numerics;
using Microsoft.JSInterop;
using Veilborne;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Web.WebImpl;

/// <summary>
/// Projects terrain heightmaps to screen-space triangles and draws them via Canvas 2D.
/// Uses a flat int buffer for fast WASM→JS interop (no per-triangle JSON objects).
/// </summary>
public sealed class WebPixiTerrainRenderer : ITerrainRenderer
{
    private const int GridStep = 8;
    private const int MaxTriangles = 1500;
    private const int FloatsPerTriangle = 7; // color, x1, y1, x2, y2, x3, y3

    private readonly IJSInProcessRuntime _js;
    private readonly IGraphicsProvider _graphics;
    private readonly List<PendingChunk> _pending = new(8);
    private readonly List<int> _triangleBuffer = new(MaxTriangles * FloatsPerTriangle);
    private int[] _jsBuffer = Array.Empty<int>();

    private Vector4 _tint = Vector4.One;
    private Vector3 _biomeColor = new(0.35f, 0.55f, 0.28f);

    private Vector3 _camPos;
    private Vector3 _camRight;
    private Vector3 _camUp;
    private Vector3 _camForward;
    private float _projScaleX;
    private float _projScaleY;
    private float _screenW;
    private float _screenH;
    private bool _cameraReady;

    public WebPixiTerrainRenderer(IJSRuntime js, IGraphicsProvider graphics)
    {
        _js = (IJSInProcessRuntime)js;
        _graphics = graphics;
    }

    public void Render(float[,] heights, float tileSize, CameraComponent camera, Vector3 baseColor)
    {
        _biomeColor = baseColor;
        RenderAt(heights, tileSize, Vector2.Zero, camera);
    }

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera) =>
        RenderAt(heights, tileSize, originWorld, camera, null, null);

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig) =>
        RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig, null);

    public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap)
    {
        if (heights == null || heights.Length == 0)
            return;

        _pending.Add(new PendingChunk(heights, tileSize, originWorld));
        EnsureCamera(camera);
    }

    public void ApplyBiomeTextures(BiomeData biome)
    {
        if (biome == null) return;
        var c = biome.Color;
        _biomeColor = new Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend)
    {
        if (primary == null) return;
        var pc = primary.Color;
        var primaryColor = new Vector3(pc.R / 255f, pc.G / 255f, pc.B / 255f);
        if (secondary != null && secondaryBlend > 0.01f)
        {
            var sc = secondary.Color;
            var secondaryColor = new Vector3(sc.R / 255f, sc.G / 255f, sc.B / 255f);
            _biomeColor = Vector3.Lerp(primaryColor, secondaryColor, Math.Clamp(secondaryBlend, 0f, 1f));
        }
        else
            _biomeColor = primaryColor;
    }

    public void SetColorTint(Vector4 color) => _tint = color;
    public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld) { }
    public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld) { }
    public void ProcessBuildQueue(int maxPerFrame) { }
    public void MarkOriginDirty(Vector2 originWorld) { }
    public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1) { }

    public void Flush()
    {
        if (_pending.Count == 0)
            return;

        _triangleBuffer.Clear();
        foreach (var chunk in _pending)
        {
            if (_triangleBuffer.Count / FloatsPerTriangle >= MaxTriangles)
                break;
            BuildTriangles(chunk);
        }

        int triCount = _triangleBuffer.Count / FloatsPerTriangle;
        if (triCount > 0)
        {
            if (_jsBuffer.Length < _triangleBuffer.Count)
                _jsBuffer = new int[_triangleBuffer.Count];
            _triangleBuffer.CopyTo(_jsBuffer, 0);
            _js.InvokeVoid("veilborne.pixi.drawTerrainBatchFlat", _jsBuffer, triCount);
        }

        _pending.Clear();
        _cameraReady = false;
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

    private void BuildTriangles(PendingChunk chunk)
    {
        var heights = chunk.Heights;
        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        if (width < 2 || depth < 2)
            return;

        int gridW = (width - 1) / GridStep + 1;
        int gridH = (depth - 1) / GridStep + 1;
        int gridCount = gridW * gridH;

        Span<int> px = gridCount <= 256 ? stackalloc int[gridCount] : new int[gridCount];
        Span<int> py = gridCount <= 256 ? stackalloc int[gridCount] : new int[gridCount];
        Span<bool> ok = gridCount <= 256 ? stackalloc bool[gridCount] : new bool[gridCount];

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
                if (_triangleBuffer.Count / FloatsPerTriangle >= MaxTriangles)
                    return;

                int i00 = gz * gridW + gx;
                int i10 = gz * gridW + gx + 1;
                int i01 = (gz + 1) * gridW + gx;
                int i11 = (gz + 1) * gridW + gx + 1;

                if (!ok[i00] && !ok[i10] && !ok[i01] && !ok[i11])
                    continue;

                int ix0 = Math.Min(gx * GridStep, width - 1);
                int iz0 = Math.Min(gz * GridStep, depth - 1);
                int ix1 = Math.Min((gx + 1) * GridStep, width - 1);
                int iz1 = Math.Min((gz + 1) * GridStep, depth - 1);

                float avgH = (heights[ix0, iz0] + heights[ix1, iz0] + heights[ix0, iz1] + heights[ix1, iz1]) * 0.25f;
                int color = HeightColorInt(avgH);

                if (ok[i00] && ok[i10] && ok[i01])
                    AddTriangle(color, px[i00], py[i00], px[i10], py[i10], px[i01], py[i01]);
                if (ok[i10] && ok[i11] && ok[i01])
                    AddTriangle(color, px[i10], py[i10], px[i11], py[i11], px[i01], py[i01]);
            }
        }
    }

    private void AddTriangle(int color, int x1, int y1, int x2, int y2, int x3, int y3)
    {
        _triangleBuffer.Add(color);
        _triangleBuffer.Add(x1);
        _triangleBuffer.Add(y1);
        _triangleBuffer.Add(x2);
        _triangleBuffer.Add(y2);
        _triangleBuffer.Add(x3);
        _triangleBuffer.Add(y3);
    }

    private int HeightColorInt(float height)
    {
        float t = Math.Clamp((height + 4f) / 48f, 0f, 1f);
        int r = (int)Math.Clamp((0.18f + t * 0.45f) * _biomeColor.X * _tint.X * 255f, 8, 255);
        int g = (int)Math.Clamp((0.42f - t * 0.18f) * _biomeColor.Y * _tint.Y * 255f, 8, 255);
        int b = (int)Math.Clamp((0.14f + t * 0.22f) * _biomeColor.Z * _tint.Z * 255f, 8, 255);
        return (r << 16) | (g << 8) | b;
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
}
