using System.Text.Json;

namespace Veilborne.Core.TerrainTexture
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
        // Packed AO/Rough map (R = AO, G = Roughness)
        public string? AOR { get; set; }
        // Individual channels (when not using a packed map)
        public string? AO { get; set; }
        public string? Rough { get; set; }
        public string? Metal { get; set; }
        // Optional height/displacement map
        public string? Displacement { get; set; }
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
        string? GetResolvedAorPath(string id);
        string? GetResolvedAoPath(string id);
        string? GetResolvedRoughPath(string id);
        string? GetResolvedMetalPath(string id);
        string? GetResolvedDisplacementPath(string id);
        float GetTileSizeOrDefault(string id, float fallback = 6f);
        IEnumerable<TerrainTextureDef> GetAll();
    }

    public sealed class TerrainTextureRegistry : ITerrainTextureRegistry
    {
        private readonly Dictionary<string, TerrainTextureDef> _defs = new(StringComparer.OrdinalIgnoreCase);

        public TerrainTextureRegistry(IEnumerable<TerrainTextureDef> defs)
        {
            foreach (var def in defs)
            {
                if (!string.IsNullOrWhiteSpace(def.Id))
                {
                    _defs[def.Id] = def;
                }
            }
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
            // NOTE: In agnostic core, we don't check for file existence. Platforms should handle this.
            if (!string.IsNullOrWhiteSpace(lod?.Strategy) && string.Equals(lod.Strategy, "Downscale", StringComparison.OrdinalIgnoreCase))
            {
                var tryCandidate = GetPreDownscaledVariantPath(rel);
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

        public string? GetResolvedAorPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.AOR;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        public string? GetResolvedAoPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.AO;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        public string? GetResolvedMetalPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.Metal;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        public string? GetResolvedDisplacementPath(string id)
        {
            var d = Get(id);
            var rel = d?.Textures?.Displacement;
            return string.IsNullOrWhiteSpace(rel) ? null : NormalizeToAssets(rel!);
        }

        private string NormalizeToAssets(string rel)
        {
            // Just normalize separators to forward slashes for internal consistency
            return rel.Replace('\\', '/');
        }

        private string? GetPreDownscaledVariantPath(string rel)
        {
            // Simple heuristic to find 2k variant path string
            try
            {
                var fname = Path.GetFileName(rel);
                var dir = Path.GetDirectoryName(rel) ?? string.Empty;
                var candidateName = fname;
                int idx = fname.LastIndexOf("_4k", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    candidateName = fname.Substring(0, idx) + "_2k" + fname.Substring(idx + 3);
                }
                else
                {
                    var stem = Path.GetFileNameWithoutExtension(fname);
                    var ext = Path.GetExtension(fname);
                    candidateName = stem + "_2k" + ext;
                }
                return Path.Combine(dir, candidateName).Replace('\\', '/');
            }
            catch
            {
                return null;
            }
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
