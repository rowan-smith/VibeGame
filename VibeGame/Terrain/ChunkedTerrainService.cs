using System.Numerics;
using Raylib_CsLo;
using VibeGame.Objects;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    public class ChunkedTerrainService : IInfiniteTerrain
    {
        private readonly ITerrainGenerator _gen;
        private readonly ITerrainRenderer _renderer;
        private readonly VibeGame.Objects.IWorldObjectRenderer _objectRenderer;
        private readonly IBiomeProvider _biomeProvider;
        private readonly Dictionary<(int cx, int cz), float[,]> _chunks = new();
        private readonly Dictionary<(int cx, int cz), List<VibeGame.Objects.SpawnedObject>> _objects = new();
        private readonly int _chunkSize;
        private readonly float _tileSize;
        private int _renderRadiusChunks = 2;
        
        // New: expose simple collider queries for solid world objects (trees)
        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            float range2 = range * range;
            var list = new List<(Vector2 center, float radius)>();
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            var (ccx, ccz) = WorldToChunk(worldPos.X, worldPos.Y);
            int radChunks = Math.Max(1, _renderRadiusChunks);
            for (int dz = -radChunks; dz <= radChunks; dz++)
            {
                for (int dx = -radChunks; dx <= radChunks; dx++)
                {
                    var key = (ccx + dx, ccz + dz);
                    if (_objects.TryGetValue(key, out var objs))
                    {
                        foreach (var obj in objs)
                        {
                            float r = obj.CollisionRadius;
                            if (r <= 0f) continue;
                            float dxw = obj.Position.X - worldPos.X;
                            float dzw = obj.Position.Z - worldPos.Y;
                            float maxDist = r + range;
                            if ((dxw * dxw + dzw * dzw) <= (maxDist * maxDist))
                            {
                                list.Add((new Vector2(obj.Position.X, obj.Position.Z), r));
                            }
                        }
                    }
                }
            }
            return list;
        }

        public ChunkedTerrainService(ITerrainGenerator gen, ITerrainRenderer renderer, VibeGame.Objects.IWorldObjectRenderer objectRenderer, IBiomeProvider biomeProvider)
        {
            _gen = gen;
            _renderer = renderer;
            _objectRenderer = objectRenderer;
            _biomeProvider = biomeProvider;
            _chunkSize = gen.TerrainSize;
            _tileSize = gen.TileSize;
        }

        public float TileSize => _tileSize;
        public int ChunkSize => _chunkSize;

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            int cx = (int)MathF.Floor(x / chunkWorld);
            int cz = (int)MathF.Floor(z / chunkWorld);
            return (cx, cz);
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            _renderRadiusChunks = radiusChunks;
            var (ccx, ccz) = WorldToChunk(worldPos.X, worldPos.Z);

            // Desired 9x9 (or (2*radius+1)^2) set of chunk keys around the player
            var desired = new HashSet<(int cx, int cz)>();
            for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
            {
                for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
                {
                    desired.Add((ccx + dx, ccz + dz));
                }
            }

            // Load/generate any missing chunks in the desired set
            foreach (var key in desired)
            {
                if (_chunks.ContainsKey(key)) continue;

                var heights = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);

                // Apply biome noise modifiers overlay for this chunk based on biome at chunk origin
                float chunkWorldSize = (_chunkSize - 1) * _tileSize;
                Vector2 origin = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                var mods = biome.Data.ProceduralData.NoiseModifiers;
                if (mods.HeightScale != 0f)
                {
                    // Map Detail [0,1] -> octaves [1..6]
                    int octaves = Math.Clamp(1 + (int)MathF.Round(mods.Detail * 5f), 1, 6);
                    float baseFreq = 0.03f; // matches TerrainGenerator TerrainScale
                    float freq = baseFreq * (mods.Frequency <= 0f ? 1f : mods.Frequency);
                    float lac = mods.Lacunarity <= 0f ? 2.0f : mods.Lacunarity;
                    float gain = mods.Persistence;

                    // Stable global seed per biome (do NOT vary per chunk to avoid seams)
                    int seed = HashCode.Combine(biome.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), 9176);
                    var overlay = new FastNoiseLiteSource(seed, FastNoiseLite.NoiseType.OpenSimplex2, freq, octaves, lac, gain);

                    for (int z = 0; z < _chunkSize; z++)
                    {
                        for (int x = 0; x < _chunkSize; x++)
                        {
                            float wx = origin.X + x * _tileSize;
                            float wz = origin.Y + z * _tileSize;
                            float n = overlay.GetValue3D(wx, 0f, wz); // [-1,1]
                            // Scale contribution by HeightScale relative to base terrain amplitude (6.0)
                            float delta = n * (mods.HeightScale * 6.0f);
                            heights[x, z] += delta;
                        }
                    }
                }

                _chunks[key] = heights;

                // Generate biome-specific world objects for this chunk and filter by actual biome membership
                if (_objectRenderer != null)
                {
                    var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _gen, heights, origin, 18);
                    var filtered = new List<VibeGame.Objects.SpawnedObject>(raw.Count);
                    foreach (var obj in raw)
                    {
                        var at = _biomeProvider.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _gen);
                        if (string.Equals(at.Id, biome.Id, StringComparison.OrdinalIgnoreCase))
                            filtered.Add(obj);
                    }
                    _objects[key] = filtered;
                }
            }

            // Evict chunks outside the desired window to cap active set to (2r+1)^2 (e.g., 81 for r=4)
            if (_chunks.Count > desired.Count)
            {
                var toRemove = _chunks.Keys.Where(k => !desired.Contains(k)).ToList();
                foreach (var k in toRemove)
                {
                    _chunks.Remove(k);
                    _objects.Remove(k);
                }
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // Match the rendered terrain by applying the same biome-dependent overlay
            // that is added in UpdateAround() when generating chunk heightmaps.
            float baseH = _gen.ComputeHeight(worldX, worldZ);

            // Biome overlay (stable per-biome seed and parameters mirroring UpdateAround)
            var biome = _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), _gen);
            var mods = biome.Data.ProceduralData.NoiseModifiers;
            if (mods.HeightScale != 0f)
            {
                int octaves = Math.Clamp(1 + (int)MathF.Round(mods.Detail * 5f), 1, 6);
                float baseFreq = 0.03f; // matches TerrainGenerator TerrainScale
                float freq = baseFreq * (mods.Frequency <= 0f ? 1f : mods.Frequency);
                float lac = mods.Lacunarity <= 0f ? 2.0f : mods.Lacunarity;
                float gain = mods.Persistence;

                int seed = HashCode.Combine(biome.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), 9176);
                var overlay = new FastNoiseLiteSource(seed, FastNoiseLite.NoiseType.OpenSimplex2, freq, octaves, lac, gain);
                float n = overlay.GetValue3D(worldX, 0f, worldZ); // [-1,1]
                float delta = n * (mods.HeightScale * 6.0f); // 6.0 ~= TerrainGenerator amplitude baseline
                baseH += delta;
            }

            return baseH;
        }

        public IBiome GetBiomeAt(float worldX, float worldZ)
        {
            return _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), _gen);
        }

        public void Render(Camera3D camera, Color baseColor)
        {
            RenderInternal(camera, baseColor, null);
        }

        // New: allow excluding certain heightmap chunk coordinates (cx, cz)
        public void RenderWithExclusions(Camera3D camera, Color baseColor, HashSet<(int cx, int cz)>? exclude)
        {
            RenderInternal(camera, baseColor, exclude);
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            var (ccx, ccz) = WorldToChunk(camera.position.X, camera.position.Z);
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;

            for (int dz = -_renderRadiusChunks; dz <= _renderRadiusChunks; dz++)
            {
                for (int dx = -_renderRadiusChunks; dx <= _renderRadiusChunks; dx++)
                {
                    var key = (ccx + dx, ccz + dz);
                    if (!_chunks.TryGetValue(key, out var heights))
                    {
                        heights = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);
                        _chunks[key] = heights;
                    }

                    // Compute min/max height for this chunk to build a wireframe box
                    float minY = float.MaxValue;
                    float maxY = float.MinValue;
                    int sx = heights.GetLength(0);
                    int sz = heights.GetLength(1);
                    for (int z = 0; z < sz; z++)
                    {
                        for (int x = 0; x < sx; x++)
                        {
                            float h = heights[x, z];
                            if (h < minY) minY = h;
                            if (h > maxY) maxY = h;
                        }
                    }
                    if (float.IsInfinity(minY) || float.IsInfinity(maxY)) { minY = 0f; maxY = 1f; }

                    Vector3 min = new Vector3(key.Item1 * chunkWorldSize, minY - 0.3f, key.Item2 * chunkWorldSize);
                    Vector3 size = new Vector3(chunkWorldSize, MathF.Max(0.6f, (maxY - minY) + 0.6f), chunkWorldSize);
                    Vector3 center = min + size * 0.5f;

                    // Color by ring distance from center chunk
                    int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
                    Color c = ring <= 1 ? new Color(0, 255, 0, 220)
                                         : (ring <= 3 ? new Color(255, 170, 0, 220) : new Color(255, 0, 0, 220));
                    Raylib.DrawCubeWires(center, size.X, size.Y, size.Z, c);
                }
            }
        }

        private void RenderInternal(Camera3D camera, Color baseColor, HashSet<(int cx, int cz)>? exclude)
        {
            var (ccx, ccz) = WorldToChunk(camera.position.X, camera.position.Z);
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
            for (int dz = -_renderRadiusChunks; dz <= _renderRadiusChunks; dz++)
            {
                for (int dx = -_renderRadiusChunks; dx <= _renderRadiusChunks; dx++)
                {
                    var key = (ccx + dx, ccz + dz);
                    if (exclude != null && exclude.Contains(key)) continue;

                    if (!_chunks.TryGetValue(key, out var heights))
                    {
                        heights = _gen.GenerateHeightsForChunk(key.Item1, key.Item2, _chunkSize);
                        _chunks[key] = heights;

                        // If heights were missing, ensure objects are also generated (biome-specific)
                        if (_objectRenderer != null && !_objects.ContainsKey(key))
                        {
                            Vector2 origin2 = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);
                            var biome2 = _biomeProvider.GetBiomeAt(origin2, _gen);
                            var raw2 = biome2.ObjectSpawner.GenerateObjects(biome2.Id, _gen, heights, origin2, 18);
                            var filtered2 = new List<VibeGame.Objects.SpawnedObject>(raw2.Count);
                            foreach (var obj in raw2)
                            {
                                var at = _biomeProvider.GetBiomeAt(new Vector2(obj.Position.X, obj.Position.Z), _gen);
                                if (string.Equals(at.Id, biome2.Id, StringComparison.OrdinalIgnoreCase))
                                    filtered2.Add(obj);
                            }
                            _objects[key] = filtered2;
                        }
                    }
                    Vector2 origin = new Vector2(key.Item1 * chunkWorldSize, key.Item2 * chunkWorldSize);

                    // Determine biome color tint for this chunk using primary palette color and lighting modifier
                    var biomeForChunk = _biomeProvider.GetBiomeAt(origin, _gen);
                    var bc = biomeForChunk.Data.ColorPalette.Primary;
                    float lm = MathF.Max(0.2f, biomeForChunk.Data.LightingModifier);
                    byte r = (byte)Math.Clamp((int)(bc.R * lm), 0, 255);
                    byte g = (byte)Math.Clamp((int)(bc.G * lm), 0, 255);
                    byte b = (byte)Math.Clamp((int)(bc.B * lm), 0, 255);
                    Color biomeColor = new Color(r, g, b, (byte)255);

                    _renderer.RenderAt(heights, _tileSize, origin, camera, biomeColor);

                    // Draw world objects for this chunk
                    if (_objectRenderer != null && _objects.TryGetValue(key, out var objs))
                    {
                        float maxDist = 180f; // meters
                        float maxDist2 = maxDist * maxDist;
                        var cam = camera.position;
                        foreach (var obj in objs)
                        {
                            float ddx = obj.Position.X - cam.X;
                            float ddz = obj.Position.Z - cam.Z;
                            if ((ddx * ddx + ddz * ddz) > maxDist2) continue;
                            _objectRenderer.DrawWorldObject(obj);
                        }
                    }
                }
            }
        }
    }
}
