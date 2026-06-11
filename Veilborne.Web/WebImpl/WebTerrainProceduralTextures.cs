using System.Numerics;
using Veilborne.TerrainTexture;

namespace Veilborne.Web.WebImpl;

/// <summary>
/// Generates small tileable terrain albedo tiles in-memory for web builds
/// (full 4K texture files are not shipped in the WASM bundle).
/// </summary>
public sealed class WebTerrainProceduralTextures
{
    private const int Size = 128;
    private readonly Dictionary<string, byte[]> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _tileWorldSize = new(StringComparer.OrdinalIgnoreCase);

    public WebTerrainProceduralTextures(ITerrainTextureRegistry registry)
    {
        foreach (var def in registry.GetAll())
        {
            if (string.IsNullOrWhiteSpace(def.Id))
                continue;

            _tileWorldSize[def.Id] = def.TileSize > 0.1f ? def.TileSize : 4f;
            _tiles[def.Id] = GenerateTile(def.Id);
        }

        Ensure("brown_mud_leaves");
        Ensure("aerial_rocks");
        Ensure("lichen_rock");
        Ensure("brown_mud");
        Ensure("rock_3");
        Ensure("snow");
    }

    public int Sample(string textureId, float worldX, float worldZ, float height, Vector3 biomeTint)
    {
        if (!_tiles.TryGetValue(textureId, out var tile))
            tile = _tiles["brown_mud_leaves"];

        float tileSize = _tileWorldSize.TryGetValue(textureId, out var ts) ? ts : 4f;
        float u = worldX / tileSize;
        float v = worldZ / tileSize;

        int rgb = SampleBilinear(tile, u, v);
        float shade = 0.68f + MathF.Min(0.32f, height * 0.01f);
        return TintRgb(rgb, biomeTint, 0.28f, shade);
    }

    private void Ensure(string id)
    {
        if (_tiles.ContainsKey(id))
            return;
        _tileWorldSize[id] = 4f;
        _tiles[id] = GenerateTile(id);
    }

    private static int SampleBilinear(byte[] tile, float u, float v)
    {
        u -= MathF.Floor(u);
        v -= MathF.Floor(v);
        if (u < 0f) u += 1f;
        if (v < 0f) v += 1f;

        float fx = u * Size - 0.5f;
        float fy = v * Size - 0.5f;
        int x0 = ((int)MathF.Floor(fx) % Size + Size) % Size;
        int y0 = ((int)MathF.Floor(fy) % Size + Size) % Size;
        int x1 = (x0 + 1) % Size;
        int y1 = (y0 + 1) % Size;
        float tx = fx - MathF.Floor(fx);
        float ty = fy - MathF.Floor(fy);

        int c00 = ReadRgb(tile, x0, y0);
        int c10 = ReadRgb(tile, x1, y0);
        int c01 = ReadRgb(tile, x0, y1);
        int c11 = ReadRgb(tile, x1, y1);
        return LerpRgb(LerpRgb(c00, c10, tx), LerpRgb(c01, c11, tx), ty);
    }

    private static int ReadRgb(byte[] tile, int x, int y)
    {
        int i = (y * Size + x) * 3;
        return (tile[i] << 16) | (tile[i + 1] << 8) | tile[i + 2];
    }

    private static int LerpRgb(int a, int b, float t)
    {
        int ar = (a >> 16) & 255, ag = (a >> 8) & 255, ab = a & 255;
        int br = (b >> 16) & 255, bg = (b >> 8) & 255, bb = b & 255;
        int r = (int)(ar + (br - ar) * t);
        int g = (int)(ag + (bg - ag) * t);
        int bl = (int)(ab + (bb - ab) * t);
        return (r << 16) | (g << 8) | bl;
    }

    private static int TintRgb(int rgb, Vector3 tint, float strength, float shade)
    {
        int r = (rgb >> 16) & 255;
        int g = (rgb >> 8) & 255;
        int b = rgb & 255;
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;
        rf = rf * (1f - strength) + tint.X * strength;
        gf = gf * (1f - strength) + tint.Y * strength;
        bf = bf * (1f - strength) + tint.Z * strength;
        rf *= shade;
        gf *= shade;
        bf *= shade;
        return ((int)Math.Clamp(rf * 255f, 0, 255) << 16)
             | ((int)Math.Clamp(gf * 255f, 0, 255) << 8)
             | (int)Math.Clamp(bf * 255f, 0, 255);
    }

    private static byte[] GenerateTile(string id)
    {
        var tile = new byte[Size * Size * 3];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float nx = x / (float)Size;
                float ny = y / (float)Size;
                float n = Fbm(nx * 5f, ny * 5f, 4);
                float n2 = Fbm(nx * 11f + 3.1f, ny * 11f - 1.7f, 2);
                float n3 = Fbm(nx * 23f, ny * 23f, 1);

                (float r, float g, float b) = id.ToLowerInvariant() switch
                {
                    "snow" => (
                        0.82f + n * 0.18f,
                        0.86f + n * 0.14f,
                        0.92f + n2 * 0.08f),
                    "aerial_rocks" => (
                        0.34f + n * 0.28f,
                        0.33f + n2 * 0.22f,
                        0.30f + n * 0.18f),
                    "lichen_rock" => (
                        0.30f + n * 0.18f,
                        0.36f + n2 * 0.22f,
                        0.28f + n * 0.12f),
                    "rock_3" => (
                        0.22f + n * 0.16f,
                        0.21f + n2 * 0.14f,
                        0.20f + n * 0.10f),
                    "brown_mud" => (
                        0.34f + n * 0.14f,
                        0.24f + n2 * 0.10f,
                        0.14f + n * 0.06f),
                    _ => ( // brown_mud_leaves / default foliage
                        0.18f + n * 0.16f,
                        0.30f + n2 * 0.22f,
                        0.12f + n * 0.10f),
                };

                if (n3 > 0.82f)
                {
                    r *= 0.75f;
                    g *= 0.75f;
                    b *= 0.75f;
                }

                int i = (y * Size + x) * 3;
                tile[i] = (byte)Math.Clamp(r * 255f, 0, 255);
                tile[i + 1] = (byte)Math.Clamp(g * 255f, 0, 255);
                tile[i + 2] = (byte)Math.Clamp(b * 255f, 0, 255);
            }
        }

        return tile;
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f;
        float amp = 0.55f;
        float freq = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += HashNoise(x * freq, y * freq) * amp;
            freq *= 2.1f;
            amp *= 0.5f;
        }
        return sum;
    }

    private static float HashNoise(float x, float y)
    {
        int ix = (int)MathF.Floor(x);
        int iy = (int)MathF.Floor(y);
        float fx = x - ix;
        float fy = y - iy;
        float a = Hash(ix, iy);
        float b = Hash(ix + 1, iy);
        float c = Hash(ix, iy + 1);
        float d = Hash(ix + 1, iy + 1);
        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);
        return Math.Clamp(
            a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy,
            0f, 1f);
    }

    private static float Hash(int x, int y)
    {
        uint n = (uint)(x * 374761393 + y * 668265263);
        n = (n ^ (n >> 13)) * 1274126177;
        return (n & 0xFFFFFF) / 16777215f;
    }
}
