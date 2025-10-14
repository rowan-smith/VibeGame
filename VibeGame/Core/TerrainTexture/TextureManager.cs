using ZeroElectric.Vinculum;
using Serilog;
using System.Collections.Concurrent;

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

        // Staged preload state
        private readonly ConcurrentQueue<(string key, Image img)> _decodedQueue = new();
        private List<(string key, string path)>? _toLoad;
        private volatile bool _preloading;
        private int _totalToLoad;
        private int _decodedCount;
        private int _uploadedCount;
        private string _preloadStage = "Idle";
        private CancellationTokenSource? _preloadCts;
        private Task? _cpuDecodeTask;

        public TextureManager(VibeGame.Terrain.ITerrainTextureRegistry terrainTextures, ITextureDownscaler downscaler)
        {
            _terrainTextures = terrainTextures;
            _downscaler = downscaler;
        }

        public void BeginPreload(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(TextureManager));
                if (_preloading) return; // already running

                _preloadCts?.Cancel();
                _preloadCts?.Dispose();
                _preloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _decodedQueue.Clear();
                _toLoad = new List<(string key, string path)>();
                _decodedCount = 0;
                _uploadedCount = 0;
                _totalToLoad = 0;
                _preloading = true;
                _preloadStage = "Enumerating";
                _preloadTask = null; // reset blocking helper
            }

            // Build list outside of long-held lock
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localList = new List<(string key, string path)>();
            var ct = _preloadCts!.Token;

            try
            {
                foreach (var def in _terrainTextures.GetAll())
                {
                    if (ct.IsCancellationRequested) break;
                    var paths = new List<string?>
                    {
                        _terrainTextures.GetResolvedAlbedoPath(def.Id),
                        _terrainTextures.GetResolvedNormalPath(def.Id),
                        _terrainTextures.GetResolvedArmPath(def.Id),
                        _terrainTextures.GetResolvedAorPath(def.Id),
                        _terrainTextures.GetResolvedAoPath(def.Id),
                        _terrainTextures.GetResolvedRoughPath(def.Id),
                        _terrainTextures.GetResolvedMetalPath(def.Id),
                        _terrainTextures.GetResolvedDisplacementPath(def.Id)
                    };

                    foreach (var rel in paths)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(rel)) continue;
                        var path = ResolveExistingPath(rel!);
                        if (!File.Exists(path)) continue;

                        string key;
                        try { key = Path.GetFullPath(path); }
                        catch { key = path; }

                        lock (_lock)
                        {
                            if (_textures.ContainsKey(key)) continue; // already loaded
                        }
                        if (!seen.Add(key)) continue;
                        localList.Add((key, path));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while enumerating textures for preload");
            }

            lock (_lock)
            {
                _toLoad = localList;
                _totalToLoad = _toLoad.Count;
                if (_totalToLoad == 0)
                {
                    _logger.Information("Preload complete: no new textures to load (cache hit)");
                    _preloading = false;
                    _preloadStage = "Complete";
                    _preloadTask = Task.CompletedTask;
                    return;
                }
                _preloadStage = $"Decoding (0/{_totalToLoad})";
            }

            int dop = Math.Max(1, Math.Min(Environment.ProcessorCount, 6));
            _cpuDecodeTask = Task.Run(() =>
            {
                try
                {
                    System.Threading.Tasks.Parallel.ForEach(
                        localList,
                        new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = dop },
                        pair =>
                        {
                            if (ct.IsCancellationRequested) return;
                            try
                            {
                                var img = _downscaler.LoadImageWithDownscale(pair.path);
                                if (img.width > 0 && img.height > 0)
                                {
                                    _decodedQueue.Enqueue((pair.key, img));
                                    Interlocked.Increment(ref _decodedCount);
                                    lock (_lock)
                                    {
                                        _preloadStage = $"Decoding ({_decodedCount}/{_totalToLoad})";
                                    }
                                }
                                else
                                {
                                    _logger.Warning("Decoded image invalid for {Path}", pair.path);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // ignore
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "CPU decode failed for {Path}", pair.path);
                            }
                        });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during CPU decode stage");
                }
            }, ct);
        }

        public void PumpPreload(int maxUploadsPerFrame = 16)
        {
            if (!_preloading) return;
            if (_preloadCts == null) return;
            var ct = _preloadCts.Token;

            // Upload up to N images to GPU
            int uploadedThisFrame = 0;
            while (uploadedThisFrame < Math.Max(1, maxUploadsPerFrame) && _decodedQueue.TryDequeue(out var item))
            {
                if (ct.IsCancellationRequested)
                {
                    try { Raylib.UnloadImage(item.img); } catch { }
                    break;
                }

                try
                {
                    var tex = Raylib.LoadTextureFromImage(item.img);
                    Raylib.UnloadImage(item.img);
                    if (tex.id == 0)
                    {
                        _logger.Warning("Failed to create texture for {Key}", item.key);
                        continue;
                    }

                    try { Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_BILINEAR); } catch { }
                    try
                    {
                        RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_S, RlGl.RL_TEXTURE_WRAP_REPEAT);
                        RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_T, RlGl.RL_TEXTURE_WRAP_REPEAT);
                    }
                    catch { }

                    lock (_lock)
                    {
                        _textures[item.key] = tex;
                    }

                    uploadedThisFrame++;
                    int u = Interlocked.Increment(ref _uploadedCount);
                    lock (_lock)
                    {
                        _preloadStage = $"Uploading ({u}/{_totalToLoad})";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed uploading texture for {Key}", item.key);
                    try { Raylib.UnloadImage(item.img); } catch { }
                }
            }

            // Complete when CPU task done and queue drained
            if ((_cpuDecodeTask?.IsCompleted ?? true) && _decodedQueue.IsEmpty && _uploadedCount >= _totalToLoad)
            {
                _preloading = false;
                _preloadStage = "Complete";
                lock (_lock)
                {
                    _preloadTask ??= Task.CompletedTask;
                }
                _logger.Information("Preload complete: {Count} textures (loaded {Delta} new)", _textures.Count, _uploadedCount);
            }
        }

        public bool IsPreloading => _preloading;

        public float PreloadProgress
        {
            get
            {
                int total = _totalToLoad;
                if (!_preloading) return 1f;
                if (total <= 0) return 0f;
                float d = MathF.Min(1f, (float)_decodedCount / Math.Max(1, total));
                float u = MathF.Min(1f, (float)_uploadedCount / Math.Max(1, total));
                return 0.5f * d + 0.5f * u;
            }
        }

        public string PreloadStage => _preloadStage;

        public Task PreloadAsync(CancellationToken cancellationToken = default)
        {
            // Blocking helper: run staged preload and pump uploads until complete on this thread.
            BeginPreload(cancellationToken);
            return Task.Run(async () =>
            {
                try
                {
                    while (IsPreloading)
                    {
                        PumpPreload(64);
                        await Task.Delay(1, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            }, CancellationToken.None);
        }

        private void LoadAll(CancellationToken ct)
        {
            try
            {
                _logger.Information("Preloading textures (parallel CPU decode + main-thread GPU upload)...");

                int before;
                lock (_lock) { before = _textures.Count; }

                // 1) Collect unique, existing file paths to load that are not already cached
                var toLoad = new List<(string key, string path)>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var def in _terrainTextures.GetAll())
                {
                    if (ct.IsCancellationRequested) break;

                    var paths = new List<string?>
                    {
                        _terrainTextures.GetResolvedAlbedoPath(def.Id),
                        _terrainTextures.GetResolvedNormalPath(def.Id),
                        _terrainTextures.GetResolvedArmPath(def.Id),
                        _terrainTextures.GetResolvedAorPath(def.Id),
                        _terrainTextures.GetResolvedAoPath(def.Id),
                        _terrainTextures.GetResolvedRoughPath(def.Id),
                        _terrainTextures.GetResolvedMetalPath(def.Id),
                        _terrainTextures.GetResolvedDisplacementPath(def.Id)
                    };

                    foreach (var rel in paths)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(rel)) continue;

                        var path = ResolveExistingPath(rel!);
                        if (!File.Exists(path)) continue;

                        string key;
                        try { key = Path.GetFullPath(path); }
                        catch { key = path; }

                        lock (_lock)
                        {
                            if (_textures.ContainsKey(key)) continue; // already loaded
                        }
                        if (!seen.Add(key)) continue; // dedupe within this preload session

                        toLoad.Add((key, path));
                    }
                }

                if (toLoad.Count == 0)
                {
                    _logger.Information("Preload complete: no new textures to load (cache hit)");
                    return;
                }

                _logger.Information("Preloading {Count} textures (CPU stage)...", toLoad.Count);

                // 2) CPU-bound stage in parallel: load + downscale to Image
                var decoded = new ConcurrentQueue<(string key, Image img)>();
                int dop = Math.Max(1, Math.Min(Environment.ProcessorCount, 6)); // cap parallelism to avoid high RAM usage

                System.Threading.Tasks.Parallel.ForEach(
                    toLoad,
                    new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = dop },
                    pair =>
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            var img = _downscaler.LoadImageWithDownscale(pair.path);
                            if (img.width > 0 && img.height > 0)
                            {
                                decoded.Enqueue((pair.key, img));
                            }
                            else
                            {
                                _logger.Warning("Decoded image invalid for {Path}", pair.path);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // ignored, cooperative cancel
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "CPU decode failed for {Path}", pair.path);
                        }
                    });

                _logger.Information("CPU stage complete. Uploading {Count} textures to GPU...", decoded.Count);

                // 3) GPU-bound stage on calling thread (must own GL context)
                while (decoded.TryDequeue(out var item))
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { Raylib.UnloadImage(item.img); } catch { }
                        break;
                    }

                    try
                    {
                        var tex = Raylib.LoadTextureFromImage(item.img);
                        // Free CPU memory immediately after upload
                        Raylib.UnloadImage(item.img);

                        if (tex.id == 0)
                        {
                            _logger.Warning("Failed to create texture for {Key}", item.key);
                            continue;
                        }

                        try
                        {
                            Raylib.SetTextureFilter(tex, TextureFilter.TEXTURE_FILTER_BILINEAR);
                        }
                        catch { _logger.Debug("Failed to set texture filter for {Key}", item.key); }

                        try
                        {
                            RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_S, RlGl.RL_TEXTURE_WRAP_REPEAT);
                            RlGl.rlTextureParameters(tex.id, RlGl.RL_TEXTURE_WRAP_T, RlGl.RL_TEXTURE_WRAP_REPEAT);
                        }
                        catch { _logger.Debug("Failed to set texture wrap for {Key}", item.key); }

                        lock (_lock)
                        {
                            _textures[item.key] = tex;
                        }
                        _logger.Information("Loaded texture {Key}: id={Id}", item.key, tex.id);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed uploading texture for {Key}", item.key);
                        try { Raylib.UnloadImage(item.img); } catch { }
                    }
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
