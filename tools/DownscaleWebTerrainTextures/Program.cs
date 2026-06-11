using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Veilborne.TerrainTexture;

const int DefaultSize = 512;

// Must match WebPixiTerrainRenderer.TextureIndex order.
string[] textureOrder =
[
    "brown_mud_leaves",
    "aerial_rocks",
    "lichen_rock",
    "brown_mud",
    "rock_3",
    "snow",
];

string workspaceRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindWorkspaceRoot();
int targetSize = args.Length > 1 && int.TryParse(args[1], out int parsed) ? Math.Clamp(parsed, 128, 1024) : DefaultSize;

string coreAssets = Path.Combine(workspaceRoot, "Veilborne.Core", "assets");
string configDir = Path.Combine(coreAssets, "config", "terrain");
string outRoot = Path.Combine(workspaceRoot, "Veilborne.Web", "wwwroot", "assets", "textures", "terrain");

if (!Directory.Exists(configDir))
{
    Console.Error.WriteLine($"[terrain-web] Config directory not found: {configDir}");
    return 1;
}

var defsById = LoadTextureDefs(configDir);
var assetRoots = BuildAssetSearchRoots(workspaceRoot, coreAssets);
var manifestEntries = new List<ManifestEntry>();

foreach (var id in textureOrder)
{
    if (!defsById.TryGetValue(id, out var def))
    {
        Console.WriteLine($"[terrain-web] Skip '{id}' — no terrain JSON.");
        continue;
    }

    int index = Array.IndexOf(textureOrder, id);
    string outDir = Path.Combine(outRoot, id);
    Directory.CreateDirectory(outDir);
    string outFile = Path.Combine(outDir, "albedo_web.jpg");
    string? source = ResolveAlbedoSource(def, assetRoots);

    string sourceKind;
    if (source != null && File.Exists(source))
    {
        DownscaleToWeb(source, outFile, targetSize);
        sourceKind = "downscaled";
        Console.WriteLine($"[terrain-web] {id}: downscaled {Path.GetFileName(source)} -> albedo_web.jpg ({targetSize}px)");
    }
    else
    {
        WriteProceduralTexture(outFile, id, targetSize);
        sourceKind = "procedural";
        Console.WriteLine($"[terrain-web] {id}: source not found, wrote procedural albedo_web.jpg ({targetSize}px)");
        if (!string.IsNullOrWhiteSpace(def.Textures?.Albedo))
            Console.WriteLine($"[terrain-web]   expected under assets/: {def.Textures.Albedo}");
    }

    manifestEntries.Add(new ManifestEntry(
        id,
        index,
        $"assets/textures/terrain/{id}/albedo_web.jpg",
        def.TileSize > 0 ? def.TileSize : 6f,
        sourceKind));
}

var manifest = new WebTerrainManifest(1, manifestEntries);
string manifestPath = Path.Combine(outRoot, "web_manifest.json");
await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
}));
Console.WriteLine($"[terrain-web] Wrote {manifestPath} ({manifestEntries.Count} textures)");
return 0;

static string FindWorkspaceRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Veilborne.Web"))
            && Directory.Exists(Path.Combine(dir.FullName, "Veilborne.Core")))
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}

static Dictionary<string, TerrainTextureDef> LoadTextureDefs(string configDir)
{
    var map = new Dictionary<string, TerrainTextureDef>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(configDir, "*.json"))
    {
        var json = File.ReadAllText(file);
        var def = JsonSerializer.Deserialize<TerrainTextureDef>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (def != null && !string.IsNullOrWhiteSpace(def.Id))
            map[def.Id] = def;
    }
    return map;
}

static List<string> BuildAssetSearchRoots(string workspaceRoot, string coreAssets)
{
    var roots = new List<string>();
    string? env = Environment.GetEnvironmentVariable("VEILBORNE_ASSETS_DIR");
    if (!string.IsNullOrWhiteSpace(env))
        roots.Add(Path.GetFullPath(env));

    roots.Add(coreAssets);

    string desktopOut = Path.Combine(workspaceRoot, "Veilborne.Desktop", "bin");
    if (Directory.Exists(desktopOut))
    {
        foreach (var assets in Directory.EnumerateDirectories(desktopOut, "assets", SearchOption.AllDirectories))
            roots.Add(assets);
    }

    roots.Add(Path.Combine(workspaceRoot, "Veilborne.Web", "wwwroot", "assets"));
    return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static string? ResolveAlbedoSource(TerrainTextureDef def, IReadOnlyList<string> assetRoots)
{
    string? rel = def.Textures?.Albedo;
    if (!string.IsNullOrWhiteSpace(rel))
    {
        foreach (var root in assetRoots)
        {
            string direct = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(direct))
                return direct;

            string nested = Path.Combine(root, "textures", "terrain", def.Id, Path.GetFileName(rel));
            if (File.Exists(nested))
                return nested;
        }
    }

    foreach (var root in assetRoots)
    {
        string dir = Path.Combine(root, "textures", "terrain", def.Id);
        string? fromDir = FindAlbedoInDirectory(dir);
        if (fromDir != null)
            return fromDir;
    }

    return null;
}

static string? FindAlbedoInDirectory(string dir)
{
    if (!Directory.Exists(dir))
        return null;

    var images = Directory.EnumerateFiles(dir)
        .Where(f =>
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg";
        })
        .ToArray();

    foreach (var f in images)
    {
        string name = Path.GetFileName(f).ToLowerInvariant();
        if (name.Contains("_diff_") || name.Contains("_albedo_") || name.Contains("_col_") || name.Contains("basecolor") || name.Contains("color") || name.Contains("_diff."))
            return f;
    }

    return images.FirstOrDefault(f =>
    {
        string n = Path.GetFileName(f).ToLowerInvariant();
        return !(n.Contains("_nor_") || n.Contains("normal") || n.Contains("_rough_") || n.Contains("rough")
            || n.Contains("_ao_") || n.Contains("ambientocclusion") || n.Contains("_metal_") || n.Contains("metallic")
            || n.Contains("_disp_") || n.Contains("height") || n.Contains("_arm_"));
    });
}

static void DownscaleToWeb(string source, string destination, int size)
{
    using var image = Image.Load<Rgba32>(source);
    if (image.Width > size || image.Height > size)
        image.Mutate(ctx => ctx.Resize(size, size));

    var encoder = new JpegEncoder { Quality = 82 };
    image.SaveAsJpeg(destination, encoder);
}

static void WriteProceduralTexture(string destination, string id, int size)
{
    var palette = id.ToLowerInvariant() switch
    {
        "snow" => ((byte)220, (byte)228, (byte)238, (byte)245, (byte)248, (byte)252, (byte)180, (byte)190, (byte)210),
        "aerial_rocks" => ((byte)88, (byte)86, (byte)80, (byte)118, (byte)114, (byte)106, (byte)52, (byte)50, (byte)48),
        "lichen_rock" => ((byte)72, (byte)82, (byte)62, (byte)98, (byte)108, (byte)78, (byte)48, (byte)58, (byte)38),
        "rock_3" => ((byte)58, (byte)56, (byte)54, (byte)82, (byte)78, (byte)72, (byte)34, (byte)32, (byte)30),
        "brown_mud" => ((byte)82, (byte)58, (byte)34, (byte)102, (byte)74, (byte)44, (byte)58, (byte)38, (byte)22),
        _ => ((byte)38, (byte)62, (byte)28, (byte)72, (byte)98, (byte)42, (byte)18, (byte)32, (byte)12),
    };

    using var image = new Image<Rgba32>(size, size);
    image.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                float nx = x / (float)size;
                float ny = y / (float)size;
                float n = Fbm(nx * 4f, ny * 4f, 4);
                float n2 = Fbm(nx * 9f + 1.3f, ny * 9f - 0.7f, 2);
                float n3 = Hash(x, y);

                byte r = (byte)Math.Clamp(palette.Item1 + (palette.Item4 - palette.Item1) * n, 0, 255);
                byte g = (byte)Math.Clamp(palette.Item2 + (palette.Item5 - palette.Item2) * n2, 0, 255);
                byte b = (byte)Math.Clamp(palette.Item3 + (palette.Item6 - palette.Item3) * n, 0, 255);
                if (n3 > 0.88f)
                {
                    r = palette.Item7;
                    g = palette.Item8;
                    b = palette.Item9;
                }

                row[x] = new Rgba32(r, g, b, 255);
            }
        }
    });

    image.SaveAsJpeg(destination, new JpegEncoder { Quality = 82 });
}

static float Fbm(float x, float y, int octaves)
{
    float sum = 0f, amp = 0.55f, freq = 1f;
    for (int i = 0; i < octaves; i++)
    {
        sum += Hash((int)MathF.Floor(x * freq * 32f), (int)MathF.Floor(y * freq * 32f)) * amp;
        freq *= 2.1f;
        amp *= 0.5f;
    }
    return sum;
}

static float Hash(int x, int y)
{
    uint n = (uint)(x * 374761393 + y * 668265263);
    n = (n ^ (n >> 13)) * 1274126177;
    return (n & 0xFFFFFF) / 16777215f;
}

file sealed record ManifestEntry(string Id, int Index, string Path, float TileSize, string Source);
file sealed record WebTerrainManifest(int Version, List<ManifestEntry> Textures);
