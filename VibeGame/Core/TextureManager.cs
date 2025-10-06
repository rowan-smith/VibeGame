using Raylib_CsLo;
using Serilog;

namespace VibeGame.Core
{
    public class TextureManager : ITextureManager
    {
        private readonly ILogger _logger = Log.ForContext<TextureManager>();
        private readonly object _lock = new object();
        private readonly Dictionary<string, Texture> _textures = new();
        private Task? _preloadTask;
        private bool _disposed;

        // Well-known keys used by the game
        public const string LowDiffuseKey = "terrain.low.diffuse";
        public const string HighDiffuseKey = "terrain.high.diffuse";

        // Paths relative to repo/game working directory
        private static readonly string LowDiffusePathRel = Path.Combine("assets", "models", "environment", "terrain", "brown_mud_leaves", "textures", "brown_mud_leaves_01_diff_4k.png");
        private static readonly string HighDiffusePathRel = Path.Combine("assets", "models", "environment", "terrain", "aerial_rocks", "textures", "aerial_rocks_04_diff_4k.png");

        public Task PreloadAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(TextureManager));
                }

                if (_preloadTask == null)
                {
                    // Perform loading on the calling thread to ensure GL context ownership
                    LoadAll(cancellationToken);
                    _preloadTask = Task.CompletedTask;
                }

                return _preloadTask;
            }
        }

        private void LoadAll(CancellationToken ct)
        {
            try
            {
                _logger.Information("Preloading textures...");

                // Resolve paths from multiple possible roots (bin/, repo/, etc.)
                var lowPath = ResolveExistingPath(LowDiffusePathRel);
                var highPath = ResolveExistingPath(HighDiffusePathRel);

                // Load textures sequentially on the current thread to respect GL context affinity
                LoadTextureIfMissing(LowDiffuseKey, lowPath, ct);
                LoadTextureIfMissing(HighDiffuseKey, highPath, ct);

                _logger.Information("Preload complete: {Count} textures", _textures.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Preload cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during preload");
            }
        }

        private static string ResolveExistingPath(string relative)
        {
            // Try current working directory
            var candidates = new List<string>();
            try { candidates.Add(Path.GetFullPath(relative)); } catch { }
            // Try alongside executable (bin folder)
            try { candidates.Add(Path.Combine(AppContext.BaseDirectory, relative)); } catch { }
            // Try moving up from bin to project folder
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relative))); } catch { }
            // Try repo root (one more up if running from nested folders)
            try { candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative))); } catch { }

            foreach (var p in candidates)
            {
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
            }
            // Fall back to the original relative path; Load will log if missing
            return relative;
        }

        private void LoadTextureIfMissing(string key, string path, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            lock (_lock)
            {
                if (_textures.ContainsKey(key)) return;
            }

            try
            {
                if (!File.Exists(path))
                {
                    _logger.Warning("File not found for key {Key}: {Path}", key, path);
                    return;
                }

                // Load image then create GPU texture
                var img = Raylib.LoadImage(path);
                var tex = Raylib.LoadTextureFromImage(img);
                Raylib.UnloadImage(img); // free CPU memory, keep GPU texture

                // Ensure reasonable filtering and repeat wrap
                try
                {
                    Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_BILINEAR);
                }
                catch
                {
                    _logger.Debug("Failed to set texture filter for {Key}", key);
                }

                try
                {
                    RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_S, RlGl.RL_TEXTURE_WRAP_REPEAT);
                    RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_T, RlGl.RL_TEXTURE_WRAP_REPEAT);
                }
                catch
                {
                    _logger.Debug("Failed to set texture wrap for {Key}", key);
                }

                if (tex.id == 0)
                {
                    _logger.Warning("Failed to create texture for {Key}", key);
                    return;
                }

                lock (_lock)
                {
                    _textures[key] = tex;
                }
                _logger.Information("Loaded texture {Key}: id={Id}", key, tex.id);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed loading key {Key} from {Path}", key, path);
            }
        }

        public bool TryGet(string key, out Texture texture)
        {
            lock (_lock)
            {
                if (_textures.TryGetValue(key, out texture))
                {
                    _logger.Debug("Texture {Key} found: id={Id}", key, texture.id);
                    return texture.id != 0;
                }
            }
            texture = default;
            return false;
        }

        public bool TryGetOrLoadByPath(string relativeOrAbsolutePath, out Texture texture)
        {
            texture = default;
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath)) return false;

            // Normalize to absolute path and use it as the cache key
            string path = Path.IsPathRooted(relativeOrAbsolutePath)
                ? relativeOrAbsolutePath
                : ResolveExistingPath(relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar));

            string key;
            try { key = Path.GetFullPath(path); }
            catch { key = path; }

            // Load if needed
            LoadTextureIfMissing(key, path, CancellationToken.None);

            // Return from cache if present
            return TryGet(key, out texture);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                foreach (var kvp in _textures)
                {
                    try
                    {
                        if (kvp.Value.id != 0)
                        {
                            _logger.Information("Unloading texture {Key}: id={Id}", kvp.Key, kvp.Value.id);
                            Raylib.UnloadTexture(kvp.Value);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error unloading texture {Key}", kvp.Key);
                    }
                }
                _textures.Clear();
            }
        }
    }
}
