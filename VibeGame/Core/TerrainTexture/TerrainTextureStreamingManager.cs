using Serilog;
using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    public sealed class TerrainTextureStreamingManager
    {
        private readonly ILogger _logger = Log.ForContext<TerrainTextureStreamingManager>();

        public float HighDetailRange { get; set; } = 80f;
        public float MidDetailRange { get; set; } = 200f;
        public float LowDetailRange { get; set; } = 400f;
        public int MaxMip { get; set; } = 3;

        public int GetTargetMip(bool editable, float distance)
        {
            if (editable) return 0;
            if (distance < HighDetailRange) return 1;
            if (distance < MidDetailRange) return 2;
            return Math.Min(MaxMip, 3);
        }

        public string GetMipPath(string basePath, int mip)
        {
            return MipGenerator.GetMipPath(basePath, mip);
        }

        public bool TryGetOrLoad(ITextureManager textureManager, string basePath, int mip, out Texture texture)
        {
            texture = default;
            if (string.IsNullOrWhiteSpace(basePath)) return false;

            // Ensure the mip file exists (generate on demand if missing)
            string mipPath = GetMipPath(basePath, mip);
            string fullMipPath = ResolveExistingPath(mipPath);
            if (!File.Exists(fullMipPath))
            {
                // Attempt to generate the chain; if generation fails, fall back to original
                string baseFull = ResolveExistingPath(basePath);
                if (!File.Exists(baseFull)) return false;
                MipGenerator.EnsureMipExists(baseFull, mip);
                fullMipPath = ResolveExistingPath(mipPath);
            }

            return textureManager.TryGetOrLoadByPath(fullMipPath, out texture);
        }

        private static string ResolveExistingPath(string relative)
        {
            var candidates = new List<string>();
            try { candidates.Add(Path.GetFullPath(relative)); } catch { }
            try { candidates.Add(Path.Combine(AppContext.BaseDirectory, relative)); } catch { }
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relative))); } catch { }
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative))); } catch { }
            foreach (var p in candidates)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }
            return relative;
        }
    }
}
