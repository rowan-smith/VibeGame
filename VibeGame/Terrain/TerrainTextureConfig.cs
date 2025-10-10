using System.Text.Json;

namespace VibeGame.Terrain
{
    // Raw definition as stored in assets\\config\\terrain JSON files
    public sealed class TerrainTextureDef
    {
        public string Id { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public TexturePaths Textures { get; set; } = new();
        public Dictionary<string, string> PackedChannels { get; set; } = new();
        public float TileSize { get; set; } = 1.0f;
        public float NoiseWeight { get; set; } = 0.0f;
        public LodOptions? LOD { get; set; }
    }

    public sealed class TexturePaths
    {
        public string? Albedo { get; set; }
        public string? Normal { get; set; }
        // Common packed map (Ambient Occlusion, Roughness, Metallic)
        public string? ARM { get; set; }
        // Separate roughness map (fallback when ARM not provided)
        public string? Rough { get; set; }
    }

    public sealed class LodOptions
    {
        public string? Strategy { get; set; } // "Downscale" or "External"
        public bool? GenerateMipmaps { get; set; }
        public int? MaxMipLevel { get; set; }
        public Dictionary<string, string>? Levels { get; set; } // for Strategy==External
    }

    public interface ITerrainTextureRegistry
    {
        TerrainTextureDef? Get(string id);
        string? GetResolvedAlbedoPath(string id);
        string? GetResolvedNormalPath(string id);
        string? GetResolvedArmPath(string id);
        string? GetResolvedRoughPath(string id);
        float GetTileSizeOrDefault(string id, float fallback = 6f);
        IEnumerable<TerrainTextureDef> GetAll();
    }

    public sealed class TerrainTextureRegistry : ITerrainTextureRegistry
    {
        private readonly Dictionary<string, TerrainTextureDef> _defs = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _assetsRoot;

        public TerrainTextureRegistry()
        {
            _assetsRoot = Path.Combine(AppContext.BaseDirectory, "assets");
            var dir = Path.Combine(_assetsRoot, "config", "terrain");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var def = Load(file);
                        if (!string.IsNullOrWhiteSpace(def.Id))
                        {
                            _defs[def.Id] = def;
                        }
                    }
                    catch
                    {
                        // ignore bad entries to avoid breaking startup
                    }
                }
            }
        }

        private static TerrainTextureDef Load(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var def = JsonSerializer.Deserialize<TerrainTextureDef>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (def == null) throw new InvalidOperationException($"Failed to deserialize terrain texture: {filePath}");
            return def;
        }

        public TerrainTextureDef? Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return _defs.TryGetValue(id, out var d) ? d : null;
        }

        public string? GetResolvedAlbedoPath(string id)
        {
            var d = Get(id);
            if (d == null) return null;

            // If using external LODs, prefer level 0 if provided
            var lod = d.LOD;
            if (!string.IsNullOrWhiteSpace(lod?.Strategy) && string.Equals(lod.Strategy, "External", StringComparison.OrdinalIgnoreCase))
            {
                var levels = lod.Levels;
                if (levels != null && levels.TryGetValue("0", out var level0Path) && !string.IsNullOrWhiteSpace(level0Path))
                {
                    return NormalizeToAssets(level0Path!);
                }
            }

            var rel = d.Textures?.Albedo;
            if (string.IsNullOrWhiteSpace(rel)) return null;

            // Prefer pre-downscaled variant if LOD Strategy is Downscale and such file exists
            if (!string.IsNullOrWhiteSpace(lod?.Strategy) && string.Equals(lod.Strategy, "Downscale", StringComparison.OrdinalIgnoreCase))
            {
                var tryCandidate = TryFindPreDownscaledVariant(rel);
                if (!string.IsNullOrWhiteSpace(tryCandidate))
                {
                    return tryCandidate;
                }
            }

            return NormalizeToAssets(rel!);
        }

        public string? GetResolvedNormalPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.Normal;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        public string? GetResolvedArmPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.ARM;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        public string? GetResolvedRoughPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.Rough;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        private string NormalizeToAssets(string rel)
        {
            // Most configs store paths relative to assets/ (e.g., "textures/terrain/...")
            var norm = rel.Replace('/', Path.DirectorySeparatorChar);
            // Ensure it is under assets root if not absolute and not already prefixed with assets
            if (!Path.IsPathRooted(norm))
            {
                if (!norm.StartsWith("assets" + Path.DirectorySeparatorChar) && !norm.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                {
                    norm = Path.Combine("assets", norm);
                }
            }
            return norm;
        }

        private string? TryFindPreDownscaledVariant(string rel)
        {
            // Replace a trailing "_4k" token with "_2k" in the filename, if present
            try
            {
                var norm = NormalizeToAssets(rel);
                var dir = Path.GetDirectoryName(norm) ?? string.Empty;
                var fname = Path.GetFileName(norm);
                var candidateName = fname;
                int idx = fname.LastIndexOf("_4k", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    candidateName = fname.Substring(0, idx) + "_2k" + fname.Substring(idx + 3);
                }
                else
                {
                    // Otherwise try a simple heuristic: insert _2k before extension
                    var stem = Path.GetFileNameWithoutExtension(fname);
                    var ext = Path.GetExtension(fname);
                    candidateName = stem + "_2k" + ext;
                }
                var candidateRel = Path.Combine(dir, candidateName);
                // Check if exists under base directory
                var full = Path.Combine(AppContext.BaseDirectory, candidateRel);
                if (File.Exists(full)) return candidateRel;
            }
            catch
            {
                // ignore errors and fall back
            }
            return null;
        }

        public float GetTileSizeOrDefault(string id, float fallback = 6f)
        {
            var d = Get(id);
            if (d == null || d.TileSize <= 0f) return fallback;
            return d.TileSize;
        }

        public IEnumerable<TerrainTextureDef> GetAll()
        {
            return _defs.Values;
        }
    }
}
