using Raylib_CsLo;
using Serilog;

namespace VibeGame.Core
{
    public class TextureManager : ITextureManager
    {
        private readonly ILogger _logger = Log.ForContext<TextureManager>();
        private readonly object _lock = new object();
        private readonly Dictionary<string, Texture> _textures = new();
        private readonly VibeGame.Terrain.ITerrainTextureRegistry _terrainTextures;
        private Task? _preloadTask;
        private bool _disposed;

        private readonly ITextureDownscaler _downscaler;

        public TextureManager(VibeGame.Terrain.ITerrainTextureRegistry terrainTextures, ITextureDownscaler downscaler)
        {
            _terrainTextures = terrainTextures;
            _downscaler = downscaler;
        }

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

                int before;
                lock (_lock) { before = _textures.Count; }

                // Preload albedo textures from terrain texture registry
                foreach (var def in _terrainTextures.GetAll())
                {
                    if (ct.IsCancellationRequested) break;
                    var rel = _terrainTextures.GetResolvedAlbedoPath(def.Id);
                    if (string.IsNullOrWhiteSpace(rel)) continue;

                    var path = ResolveExistingPath(rel);
                    string key;
                    try { key = Path.GetFullPath(path); }
                    catch { key = path; }

                    LoadTextureIfMissing(key, path, ct);
                }

                int after;
                lock (_lock) { after = _textures.Count; }
                _logger.Information("Preload complete: {Count} textures (loaded {Delta} new)", after, after - before);
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

                // Load image via selected downscaler (may perform runtime downscale)
                var img = _downscaler.LoadImageWithDownscale(path);
                
                
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
                    // Downgrade noisy cache-hit logging to Verbose to avoid hot-path spam
                    _logger.Verbose("Texture {Key} found: id={Id}", key, texture.id);
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
