using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ZeroElectric.Vinculum;
using Veilborne.Core.GameWorlds.Terrain;
using Veilborne.Core.Interfaces;
using VibeGame.Biomes;
using VibeGame.Interfaces;

namespace VibeGame.Terrain
{
    public class TerrainManager : IEditableTerrain
    {
        private readonly EditableTerrainService _editableRing;
        private readonly ReadOnlyTerrainService _readOnlyRing;
        private readonly LowLodTerrainService? _lowLodRing;
        private readonly TerrainRingConfig _cfg;
        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;

        // Adaptive state
        private Vector3 _lastCameraPos;
        private bool _hasLast;
        private float _avgDt;
        private float _speedMps;
        private Vector3 _lastMoveDirXZ; // normalized last horizontal movement direction

        // Frame pacing
        private int _frameCounter;

        // Current radii after adaptation (debug/inspection)
        private int _curEditable;
        private int _curReadOnly;
        private int _curLowLod;

        // Debounce radii to avoid thrashing when stationary
        private int _lastEditableRadius = -1;
        private int _lastReadOnlyRadius = -1;
        private int _lastLowLodRadius = -1;

        public TerrainManager(
            EditableTerrainService editableRing,
            ReadOnlyTerrainService readOnlyRing,
            TerrainRingConfig cfg,
            IBiomeProvider biomeProvider,
            ITerrainRenderer renderer,
            LowLodTerrainService? lowLodRing = null)
        {
            _editableRing = editableRing;
            _readOnlyRing = readOnlyRing;
            _cfg = cfg;
            _lowLodRing = lowLodRing;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
        }

        public float TileSize => _readOnlyRing.TileSize;
        public int ChunkSize => _readOnlyRing.ChunkSize;

        // -----------------------------
        // Update
        // -----------------------------
        public void UpdateAround(Vector3 worldPos, int _)
        {
            // --- Adaptive radii calculation ---
            float dt = Raylib.GetFrameTime();
            if (dt <= 0f) dt = 1f / 60f;
            _avgDt = Lerp(_avgDt <= 0 ? dt : _avgDt, dt, 0.1f);

            if (_hasLast)
            {
                float d = Vector3.Distance(worldPos, _lastCameraPos);
                float instSpeed = d / MathF.Max(dt, 1e-4f);
                _speedMps = Lerp(_speedMps, instSpeed, 0.2f);

                // Track last horizontal movement direction (XZ)
                Vector3 delta = worldPos - _lastCameraPos;
                delta.Y = 0f;
                float len = delta.Length();
                if (len > 1e-3f)
                {
                    _lastMoveDirXZ = delta / len;
                }
            }
            _lastCameraPos = worldPos;
            _hasLast = true;

            // Terrain density via biome roughness/vegetation
            var adapter = new ReadOnlyTerrainAdapter(_readOnlyRing);
            var biome = _biomeProvider.GetBiomeAt(new Vector2(worldPos.X, worldPos.Z), adapter);
            float rough = biome?.Data?.ProceduralData?.Base?.Roughness ?? 0.5f;
            float veg = biome?.Data?.ProceduralData?.Base?.VegetationDensity ?? 0.5f;
            float density = Clamp01((rough + veg) * 0.5f);

            // GPU headroom heuristic from FPS vs target
            float fps = 1f / MathF.Max(_avgDt, 1e-4f);
            float perfDeficit = MathF.Max(0f, (_cfg.FpsTarget - fps) / MathF.Max(_cfg.FpsTarget, 1f));

            // Speed contribution in chunks (scale primarily RO/LOD)
            float speedChunks = _speedMps * _cfg.SpeedScale;

            int baseEdit = _cfg.EditableRadius;
            int baseRO = _cfg.ReadOnlyRadius;
            int baseLOD = _cfg.LowLodRadius;

            // Editable: keep tight around player, slight expansion if moving very fast
            int e = baseEdit + (speedChunks > 3f ? 1 : 0);
            e = Clamp(e, _cfg.MinEditable, _cfg.MaxEditable);

            // Read-only: expand with speed, reduce for density/perf
            int ro = baseRO + (int)MathF.Round(speedChunks * 1.0f) - (int)MathF.Round((density - 0.5f) * _cfg.DensityPenalty);
            ro -= (int)MathF.Round(perfDeficit * 3f);
            ro = Clamp(ro, _cfg.MinReadOnly, _cfg.MaxReadOnly);

            // Low LOD: larger expansion with speed; also contract on perf
            int lod = baseLOD + (int)MathF.Round(speedChunks * 2.0f) - (int)MathF.Round((density - 0.5f) * _cfg.DensityPenalty);
            lod -= (int)MathF.Round(perfDeficit * 5f);
            lod = Math.Max(lod, ro + 1); // ensure far ring stays outside mid ring
            lod = Clamp(lod, _cfg.MinLowLod, _cfg.MaxLowLod);

            // Debounce: if camera hasn't moved perceptibly, keep previous radii to avoid thrashing
            if (_hasLast)
            {
                float dStill = Vector3.Distance(worldPos, _lastCameraPos);
                if (dStill < 0.01f)
                {
                    if (_lastEditableRadius >= 0) e = _lastEditableRadius;
                    if (_lastReadOnlyRadius >= 0) ro = _lastReadOnlyRadius;
                    if (_lastLowLodRadius >= 0) lod = _lastLowLodRadius;
                }
            }

            // Store for next frame and expose current
            _lastEditableRadius = e;
            _lastReadOnlyRadius = ro;
            _lastLowLodRadius = lod;
            _curEditable = e; _curReadOnly = ro; _curLowLod = lod;

            // --- Predict forward position for preloading ---
            Vector3 predictedPos = worldPos;
            if (_lastMoveDirXZ != Vector3.Zero)
            {
                float lookAheadMeters = _speedMps * 1.0f; // 1 second lookahead
                predictedPos += _lastMoveDirXZ * lookAheadMeters;
            }

            // --- Update rings with computed radii ---
            // Preload mid/far rings ahead of motion to reduce pop-in
            _readOnlyRing.UpdateAround(predictedPos, ro);
            _editableRing.UpdateAround(worldPos, e);

            // Stagger ReadOnly and LowLod updates to distribute workload
            _frameCounter++;
            int roInterval = Math.Max(1, _cfg.ReadOnlyUpdateInterval);
            int lodInterval = Math.Max(1, roInterval * 2);

            if (_frameCounter % roInterval == 0)
            {
                _readOnlyRing.UpdateAround(worldPos, ro);
            }

            if (_lowLodRing is not null)
            {
                _lowLodRing.InnerExclusionRadiusChunks = Math.Max(0, ro);
                if (_frameCounter % lodInterval == 0)
                {
                    _lowLodRing.UpdateAround(predictedPos, lod);
                }
            }

            // --- Mesh generation ---
            foreach (var chunk in _readOnlyRing.GetLoadedChunks().Values)
            {
                float minX = chunk.Origin.X;
                float minZ = chunk.Origin.Y;
                float maxX = minX + _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
                float maxZ = minZ + _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
                int srcVer = _editableRing.GetMaxVersionForBounds(minX, minZ, maxX, maxZ);
                if (!chunk.IsMeshGenerated)
                {
                    _renderer.EnqueueBuild(chunk.Heights, _readOnlyRing.TileSize, chunk.Origin);
                    chunk.IsMeshGenerated = true;
                    chunk.BuiltFromVersion = srcVer;
                }
                else if (chunk.BuiltFromVersion != srcVer)
                {
                    // Patch only dirty regions overlapping this RO chunk
                    var eChunks = _editableRing.GetLoadedChunks();
                    float eChunkWorld = _editableRing.ChunkSize * _editableRing.TileSize;
                    int minEcx = (int)MathF.Floor(minX / eChunkWorld);
                    int maxEcx = (int)MathF.Floor((maxX - 1e-3f) / eChunkWorld);
                    int minEcz = (int)MathF.Floor(minZ / eChunkWorld);
                    int maxEcz = (int)MathF.Floor((maxZ - 1e-3f) / eChunkWorld);

                    bool anyPatched = false;
                    int aggX0 = int.MaxValue, aggZ0 = int.MaxValue, aggX1 = int.MinValue, aggZ1 = int.MinValue;

                    for (int cz = minEcz; cz <= maxEcz; cz++)
                    for (int cx = minEcx; cx <= maxEcx; cx++)
                    {
                        if (!eChunks.TryGetValue((cx, cz), out var ech) || !ech.Dirty) continue;
                        if (!ech.TryGetDirtyRect(out int dx0, out int dz0, out int dx1, out int dz1)) continue;

                        // Convert editable local rect to world
                        float rwMinX = ech.Origin.X + dx0 * _editableRing.TileSize;
                        float rwMaxX = ech.Origin.X + dx1 * _editableRing.TileSize;
                        float rwMinZ = ech.Origin.Y + dz0 * _editableRing.TileSize;
                        float rwMaxZ = ech.Origin.Y + dz1 * _editableRing.TileSize;

                        // Intersect with this RO chunk bounds in world
                        float iwMinX = MathF.Max(rwMinX, minX);
                        float iwMaxX = MathF.Min(rwMaxX, maxX);
                        float iwMinZ = MathF.Max(rwMinZ, minZ);
                        float iwMaxZ = MathF.Min(rwMaxZ, maxZ);
                        if (iwMinX > iwMaxX || iwMinZ > iwMaxZ) continue;

                        // Convert world intersection to RO local grid indices
                        int rx0 = Math.Clamp((int)MathF.Floor((iwMinX - minX) / _readOnlyRing.TileSize), 0, _readOnlyRing.ChunkSize);
                        int rz0 = Math.Clamp((int)MathF.Floor((iwMinZ - minZ) / _readOnlyRing.TileSize), 0, _readOnlyRing.ChunkSize);
                        int rx1 = Math.Clamp((int)MathF.Ceiling((iwMaxX - minX) / _readOnlyRing.TileSize), 0, _readOnlyRing.ChunkSize);
                        int rz1 = Math.Clamp((int)MathF.Ceiling((iwMaxZ - minZ) / _readOnlyRing.TileSize), 0, _readOnlyRing.ChunkSize);

                        for (int z = rz0; z <= rz1; z++)
                        for (int x = rx0; x <= rx1; x++)
                        {
                            float wx = minX + x * _readOnlyRing.TileSize;
                            float wz = minZ + z * _readOnlyRing.TileSize;
                            chunk.Heights[x, z] = _editableRing.SampleHeight(wx, wz);
                        }

                        anyPatched = true;
                        aggX0 = Math.Min(aggX0, rx0);
                        aggZ0 = Math.Min(aggZ0, rz0);
                        aggX1 = Math.Max(aggX1, rx1);
                        aggZ1 = Math.Max(aggZ1, rz1);
                    }

                    if (anyPatched)
                    {
                        _renderer.PatchRegion(chunk.Heights, _readOnlyRing.TileSize, chunk.Origin, aggX0, aggZ0, aggX1, aggZ1);
                        chunk.BuiltFromVersion = srcVer;
                    }
                    else
                    {
                        // Fallback to full rebuild if we couldn't compute any patch rects
                        _renderer.EnqueueBuild(chunk.Heights, _readOnlyRing.TileSize, chunk.Origin);
                        chunk.IsMeshGenerated = true;
                        chunk.BuiltFromVersion = srcVer;
                    }
                }
            }

            foreach (var chunk in _editableRing.GetLoadedChunks().Values)
            {
                int srcVer = chunk.Version;
                if (!chunk.IsMeshGenerated || chunk.BuiltFromVersion != srcVer)
                {
                    _renderer.EnqueueBuild(chunk.Heights, _editableRing.TileSize, chunk.Origin);
                    chunk.IsMeshGenerated = true;
                    chunk.BuiltFromVersion = srcVer;
                    chunk.Dirty = false;
                }
            }

            if (_lowLodRing is not null)
            {
                foreach (var chunk in _lowLodRing.GetLoadedChunks().Values)
                {
                    float minX = chunk.Origin.X;
                    float minZ = chunk.Origin.Y;
                    float maxX = minX + _lowLodRing.ChunkSize * _lowLodRing.TileSize;
                    float maxZ = minZ + _lowLodRing.ChunkSize * _lowLodRing.TileSize;
                    int srcVer = _editableRing.GetMaxVersionForBounds(minX, minZ, maxX, maxZ);
                    if (!chunk.IsMeshGenerated)
                    {
                        _renderer.EnqueueBuild(chunk.Heights, _lowLodRing.TileSize, chunk.Origin);
                        chunk.IsMeshGenerated = true;
                        chunk.BuiltFromVersion = srcVer;
                    }
                    else if (chunk.BuiltFromVersion != srcVer)
                    {
                        // Patch only dirty regions overlapping this LOD chunk
                        var eChunks = _editableRing.GetLoadedChunks();
                        float eChunkWorld = _editableRing.ChunkSize * _editableRing.TileSize;
                        int minEcx = (int)MathF.Floor(minX / eChunkWorld);
                        int maxEcx = (int)MathF.Floor((maxX - 1e-3f) / eChunkWorld);
                        int minEcz = (int)MathF.Floor(minZ / eChunkWorld);
                        int maxEcz = (int)MathF.Floor((maxZ - 1e-3f) / eChunkWorld);

                        bool anyPatched = false;
                        int aggX0 = int.MaxValue, aggZ0 = int.MaxValue, aggX1 = int.MinValue, aggZ1 = int.MinValue;

                        for (int cz = minEcz; cz <= maxEcz; cz++)
                        for (int cx = minEcx; cx <= maxEcx; cx++)
                        {
                            if (!eChunks.TryGetValue((cx, cz), out var ech) || !ech.Dirty) continue;
                            if (!ech.TryGetDirtyRect(out int dx0, out int dz0, out int dx1, out int dz1)) continue;

                            // Convert editable local rect to world
                            float rwMinX = ech.Origin.X + dx0 * _editableRing.TileSize;
                            float rwMaxX = ech.Origin.X + dx1 * _editableRing.TileSize;
                            float rwMinZ = ech.Origin.Y + dz0 * _editableRing.TileSize;
                            float rwMaxZ = ech.Origin.Y + dz1 * _editableRing.TileSize;

                            // Intersect with this LOD chunk bounds in world
                            float iwMinX = MathF.Max(rwMinX, minX);
                            float iwMaxX = MathF.Min(rwMaxX, maxX);
                            float iwMinZ = MathF.Max(rwMinZ, minZ);
                            float iwMaxZ = MathF.Min(rwMaxZ, maxZ);
                            if (iwMinX > iwMaxX || iwMinZ > iwMaxZ) continue;

                            // Convert world intersection to LOD local grid indices
                            int rx0 = Math.Clamp((int)MathF.Floor((iwMinX - minX) / _lowLodRing.TileSize), 0, _lowLodRing.ChunkSize);
                            int rz0 = Math.Clamp((int)MathF.Floor((iwMinZ - minZ) / _lowLodRing.TileSize), 0, _lowLodRing.ChunkSize);
                            int rx1 = Math.Clamp((int)MathF.Ceiling((iwMaxX - minX) / _lowLodRing.TileSize), 0, _lowLodRing.ChunkSize);
                            int rz1 = Math.Clamp((int)MathF.Ceiling((iwMaxZ - minZ) / _lowLodRing.TileSize), 0, _lowLodRing.ChunkSize);

                            for (int z = rz0; z <= rz1; z++)
                            for (int x = rx0; x <= rx1; x++)
                            {
                                float wx = minX + x * _lowLodRing.TileSize;
                                float wz = minZ + z * _lowLodRing.TileSize;
                                chunk.Heights[x, z] = _editableRing.SampleHeight(wx, wz);
                            }

                            anyPatched = true;
                            aggX0 = Math.Min(aggX0, rx0);
                            aggZ0 = Math.Min(aggZ0, rz0);
                            aggX1 = Math.Max(aggX1, rx1);
                            aggZ1 = Math.Max(aggZ1, rz1);
                        }

                        if (anyPatched)
                        {
                            _renderer.PatchRegion(chunk.Heights, _lowLodRing.TileSize, chunk.Origin, aggX0, aggZ0, aggX1, aggZ1);
                            chunk.BuiltFromVersion = srcVer;
                        }
                        else
                        {
                            // Fallback to full rebuild
                            _renderer.EnqueueBuild(chunk.Heights, _lowLodRing.TileSize, chunk.Origin);
                            chunk.IsMeshGenerated = true;
                            chunk.BuiltFromVersion = srcVer;
                        }
                    }
                }
            }

            // Upload a limited number of prepared meshes this frame
            _renderer.ProcessBuildQueue(_cfg.MaxMeshBuildsPerFrame);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // -----------------------------
        // Sample Height
        // -----------------------------
        public float SampleHeight(Vector3 worldPos)
        {
            return _editableRing.SampleHeight(worldPos.X, worldPos.Z);
        }

        // -----------------------------
        // Render
        // -----------------------------
        public void Render(Camera3D camera)
        {
            // Far
            _lowLodRing?.Render(camera);

            // Mid
            var exclude = new HashSet<(int cx, int cz)>();
            // Convert the editable radius (in editable chunks) to read-only chunks using world-units
            // This avoids over-excluding mid-ring tiles and causing visible gaps between rings.
            {
                float roChunkWorld = _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;   // world meters per RO chunk
                float eChunkWorld = _editableRing.ChunkSize * _editableRing.TileSize;    // world meters per E chunk
                int excludeInner = Math.Max(0, (int)MathF.Floor((_curEditable * eChunkWorld) / roChunkWorld) - 1);
                if (excludeInner > 0)
                {
                    int ccx = (int)MathF.Floor(camera.position.X / roChunkWorld);
                    int ccz = (int)MathF.Floor(camera.position.Z / roChunkWorld);
                    for (int dz = -_curReadOnly; dz <= _curReadOnly; dz++)
                    for (int dx = -_curReadOnly; dx <= _curReadOnly; dx++)
                    {
                        int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
                        if (ring <= excludeInner) exclude.Add((ccx + dx, ccz + dz));
                    }
                }
            }
            _readOnlyRing.RenderTiles(camera, exclude);

            // Near
            _editableRing.Render(camera);
        }

        // -----------------------------
        // Interface explicit implementations
        // -----------------------------
        void IInfiniteTerrain.RenderWithExclusions(Veilborne.Core.GameWorlds.Terrain.Camera camera, HashSet<(int cx, int cz)> exclude)
        {
            _readOnlyRing.RenderTiles(camera.RaylibCamera, exclude);
        }

        void IDebugTerrain.RenderDebugChunkBounds(Veilborne.Core.GameWorlds.Terrain.Camera camera)
        {
            _lowLodRing?.RenderDebugChunkBounds(camera.RaylibCamera);
            _readOnlyRing.RenderDebugChunkBounds(camera.RaylibCamera);
            _editableRing.RenderDebugChunkBounds(camera.RaylibCamera);
        }

        void IInfiniteTerrain.UpdateCenter(Vector3 cameraPosition) => UpdateAround(cameraPosition, 0);

        void IInfiniteTerrain.Update()
        {
            /* no-op */
        }

        void IInfiniteTerrain.Render(Veilborne.Core.GameWorlds.Terrain.Camera camera)
            => Render(camera.RaylibCamera);

        float IInfiniteTerrain.SampleHeight(Vector3 worldPos) => SampleHeight(worldPos);

        // -----------------------------
        // Editable terrain API
        // -----------------------------
        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine)
            => _editableRing.DigSphereAsync(worldCenter, radius, strength, falloff);

        public Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff)
            => _editableRing.PlaceSphereAsync(position, radius, strength, falloff);

        public TerrainDebugInfo GetDebugInfo(Vector3 worldPos)
        {
            float chunkWorld = ChunkSize * TileSize;
            int cx = (int)MathF.Floor(worldPos.X / chunkWorld);
            int cz = (int)MathF.Floor(worldPos.Z / chunkWorld);
            float modX = worldPos.X - cx * chunkWorld;
            float modZ = worldPos.Z - cz * chunkWorld;
            int localX = (int)MathF.Floor(modX / TileSize);
            int localZ = (int)MathF.Floor(modZ / TileSize);
            var adapter = new ReadOnlyTerrainAdapter(_readOnlyRing);
            var biome = _biomeProvider.GetBiomeAt(new Vector2(worldPos.X, worldPos.Z), adapter);
            string biomeId = biome?.Data.DisplayName ?? "Unknown";
            return new TerrainDebugInfo(cx, cz, localX, localZ, ChunkSize, TileSize, biomeId, worldPos);
        }

        public async Task PumpAsyncJobs()
        {
            // Pump async edit jobs (editable ring)
            await _editableRing.PumpAsyncJobs();
            // Pump RO/LOD background height sampling jobs so chunks appear when ready
            if (_readOnlyRing is not null && _readOnlyRing is ReadOnlyTerrainService ro)
                await ro.PumpAsyncJobs();
            if (_lowLodRing is not null)
                await _lowLodRing.PumpAsyncJobs();
        }
    }
}
