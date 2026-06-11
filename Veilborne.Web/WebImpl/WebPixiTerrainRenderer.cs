using System.Numerics;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Veilborne;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Web.WebImpl;

internal sealed class TerrainTriangleCommand
{
    [JsonPropertyName("c")]
    public string Color { get; set; } = "#000000";

    [JsonPropertyName("p")]
    public int[] Points { get; set; } = Array.Empty<int>();
}

/// <summary>
/// Projects terrain heightmaps to screen-space triangles and draws them via PixiJS.
/// </summary>
public sealed class WebPixiTerrainRenderer : ITerrainRenderer
{
    private const int GridStep = 2;

    private readonly IJSInProcessRuntime _js;
    private readonly IGraphicsProvider _graphics;
    private readonly List<PendingChunk> _pending = new(8);
    private Vector4 _tint = Vector4.One;
    private Vector3 _biomeColor = new(0.35f, 0.55f, 0.28f);

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

        _pending.Add(new PendingChunk(heights, tileSize, originWorld, camera));
    }

    public void ApplyBiomeTextures(BiomeData biome)
    {
        if (biome == null)
            return;

        var c = biome.Color;
        _biomeColor = new Vector3(c.R / 255f, c.G / 255f, c.B / 255f);
    }

    public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend)
    {
        if (primary == null)
            return;

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

        var triangles = new List<TerrainTriangleCommand>(2048);
        foreach (var chunk in _pending)
            BuildTriangles(chunk, triangles);

        if (triangles.Count > 0)
            _js.InvokeVoid("veilborne.pixi.drawTerrainBatch", triangles);

        _pending.Clear();
    }

    private void BuildTriangles(PendingChunk chunk, List<TerrainTriangleCommand> triangles)
    {
        var heights = chunk.Heights;
        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        if (width < 2 || depth < 2)
            return;

        var projected = new Vector2[width, depth];
        var visible = new bool[width, depth];
        int visibleCount = 0;

        for (int iz = 0; iz < depth; iz++)
        {
            for (int ix = 0; ix < width; ix++)
            {
                float wx = chunk.Origin.X + ix * chunk.TileSize;
                float wz = chunk.Origin.Y + iz * chunk.TileSize;
                float wy = heights[ix, iz];
                if (TryProject(new Vector3(wx, wy, wz), chunk.Camera, out var screen))
                {
                    projected[ix, iz] = screen;
                    visible[ix, iz] = true;
                    visibleCount++;
                }
            }
        }

        if (visibleCount == 0)
            return;

        for (int iz = 0; iz < depth - 1; iz += GridStep)
        {
            for (int ix = 0; ix < width - 1; ix += GridStep)
            {
                int ix1 = Math.Min(ix + GridStep, width - 1);
                int iz1 = Math.Min(iz + GridStep, depth - 1);

                if (!visible[ix, iz] && !visible[ix1, iz] && !visible[ix, iz1] && !visible[ix1, iz1])
                    continue;

                float h00 = heights[ix, iz];
                float h10 = heights[ix1, iz];
                float h01 = heights[ix, iz1];
                float h11 = heights[ix1, iz1];
                float avgH = (h00 + h10 + h01 + h11) * 0.25f;

                string color = HeightColor(avgH);

                AddTriangle(triangles, visible[ix, iz], projected[ix, iz], visible[ix1, iz], projected[ix1, iz], visible[ix, iz1], projected[ix, iz1], color);
                AddTriangle(triangles, visible[ix1, iz], projected[ix1, iz], visible[ix1, iz1], projected[ix1, iz1], visible[ix, iz1], projected[ix, iz1], color);
            }
        }
    }

    private static void AddTriangle(
        List<TerrainTriangleCommand> triangles,
        bool aOk, Vector2 a,
        bool bOk, Vector2 b,
        bool cOk, Vector2 c,
        string color)
    {
        if (!aOk || !bOk || !cOk)
            return;

        triangles.Add(new TerrainTriangleCommand
        {
            Color = color,
            Points = new[]
            {
                (int)MathF.Round(a.X), (int)MathF.Round(a.Y),
                (int)MathF.Round(b.X), (int)MathF.Round(b.Y),
                (int)MathF.Round(c.X), (int)MathF.Round(c.Y),
            }
        });
    }

    private string HeightColor(float height)
    {
        float t = Math.Clamp((height + 4f) / 48f, 0f, 1f);
        float r = (0.18f + t * 0.45f) * _biomeColor.X * _tint.X;
        float g = (0.42f - t * 0.18f) * _biomeColor.Y * _tint.Y;
        float b = (0.14f + t * 0.22f) * _biomeColor.Z * _tint.Z;
        int ri = (int)Math.Clamp(r * 255f, 0, 255);
        int gi = (int)Math.Clamp(g * 255f, 0, 255);
        int bi = (int)Math.Clamp(b * 255f, 0, 255);
        return $"#{ri:X2}{gi:X2}{bi:X2}";
    }

    private bool TryProject(Vector3 world, CameraComponent camera, out Vector2 screen)
    {
        var forward = Vector3.Normalize(camera.Target - camera.Position);
        if (forward.LengthSquared() < 1e-6f)
        {
            screen = Vector2.Zero;
            return false;
        }

        var right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));

        var rel = world - camera.Position;
        float xView = Vector3.Dot(rel, right);
        float yView = Vector3.Dot(rel, up);
        float zView = Vector3.Dot(rel, forward);
        if (zView <= 0.05f)
        {
            screen = Vector2.Zero;
            return false;
        }

        float aspect = Math.Max(1f, _graphics.ScreenWidth) / Math.Max(1f, _graphics.ScreenHeight);
        float fovRad = camera.FovY * (MathF.PI / 180f);
        float tanHalf = MathF.Tan(fovRad * 0.5f);
        if (tanHalf <= 1e-5f)
        {
            screen = Vector2.Zero;
            return false;
        }

        float xNdc = xView / (zView * tanHalf * aspect);
        float yNdc = yView / (zView * tanHalf);
        if (xNdc < -1.6f || xNdc > 1.6f || yNdc < -1.6f || yNdc > 1.6f)
        {
            screen = Vector2.Zero;
            return false;
        }

        float sx = (xNdc * 0.5f + 0.5f) * _graphics.ScreenWidth;
        float sy = (1f - (yNdc * 0.5f + 0.5f)) * _graphics.ScreenHeight;
        screen = new Vector2(sx, sy);
        return true;
    }

    private readonly record struct PendingChunk(float[,] Heights, float TileSize, Vector2 Origin, CameraComponent Camera);
}
