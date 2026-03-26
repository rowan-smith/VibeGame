using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Veilborne.Core.Ecs;
using Veilborne.Interfaces;
using Veilborne.Biomes;
using Veilborne.Core;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Settings;

namespace Veilborne.Terrain
{
    public class TerrainManager : IEditableTerrain, ITerrainGenerator
    {
        private readonly EditableTerrainService _editableRing;
        private readonly ReadOnlyTerrainService _readOnlyRing;
        private readonly LowLodTerrainService? _lowLodRing;
        private readonly TerrainRingConfig _cfg;
        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly IWorldConfigService _configService;
        private readonly ITimeService _time;
        private readonly IGameSettingsService _settings;
        private readonly TerrainHeightmapCache _heightmapCache = new(64);

        // Adaptive state
        private Vector3 _lastCameraPos;
        private bool _hasLast;
        private float _avgDt;
        private float _speedMps;
        private Vector3 _lastMoveDirXZ; // normalized last horizontal movement direction

        // Frame pacing
        private int _frameCounter;
        private bool _isWarmupMode;

        // Current radii after adaptation (debug/inspection)
        private int _curEditable;
        private int _curReadOnly;
        private int _curLowLod;

        // Debounce radii to avoid thrashing when stationary
        private int _lastEditableRadius = -1;
        private int _lastReadOnlyRadius = -1;
        private int _lastLowLodRadius = -1;

        public readonly record struct TerrainLoadingProgress(
            float Progress01,
            string Stage,
            int DesiredChunks,
            int LoadedChunks,
            int GeneratingChunks,
            int LoadedEntities);

        public TerrainManager(
            EditableTerrainService editableRing,
            ReadOnlyTerrainService readOnlyRing,
            TerrainRingConfig cfg,
            IBiomeProvider biomeProvider,
            ITerrainRenderer renderer,
            IWorldConfigService configService,
            ITimeService time,
            IGameSettingsService settings,
            LowLodTerrainService? lowLodRing = null)
        {
            _editableRing = editableRing;
            _readOnlyRing = readOnlyRing;
            _cfg = cfg;
            _lowLodRing = lowLodRing;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _configService = configService;
            _time = time;
            _settings = settings;
            _readOnlyRing.MaxConcurrentJobs = Math.Max(1, _cfg.MaxReadOnlyConcurrentJobs);
            if (_lowLodRing is not null)
                _lowLodRing.MaxConcurrentJobs = Math.Max(1, _cfg.MaxLowLodConcurrentJobs);
        }

        public float TileSize => _readOnlyRing.TileSize;
        public int ChunkSize => _readOnlyRing.ChunkSize;
        public int TerrainSize => ChunkSize;

        public float ComputeHeight(float worldX, float worldZ) => SampleHeight(new Vector3(worldX, 0, worldZ));

        public float[,] GenerateHeights()
        {
            float[,] heights = new float[ChunkSize, ChunkSize];
            for (int z = 0; z < ChunkSize; z++)
            for (int x = 0; x < ChunkSize; x++)
                heights[x, z] = ComputeHeight(x * TileSize, z * TileSize);
            return heights;
        }

        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            int sourceVersion = _editableRing.GetMaxVersionForBounds(
                chunkX * chunkSize * TileSize,
                chunkZ * chunkSize * TileSize,
                (chunkX + 1) * chunkSize * TileSize,
                (chunkZ + 1) * chunkSize * TileSize);

            return _heightmapCache.GetOrCreate((chunkX, chunkZ), chunkSize, TileSize, sourceVersion, () =>
            {
                float[,] heights = new float[chunkSize + 1, chunkSize + 1];
                float originX = chunkX * chunkSize * TileSize;
                float originZ = chunkZ * chunkSize * TileSize;
                for (int z = 0; z <= chunkSize; z++)
                for (int x = 0; x <= chunkSize; x++)
                    heights[x, z] = ComputeHeight(originX + x * TileSize, originZ + z * TileSize);
                return heights;
            });
        }

        public float SampleHeight(float[,] heights, float worldX, float worldZ)
        {
            int size = heights.GetLength(0);
            float gx = worldX / TileSize;
            float gz = worldZ / TileSize;
            int x0 = Math.Clamp((int)MathF.Floor(gx), 0, size - 1);
            int z0 = Math.Clamp((int)MathF.Floor(gz), 0, size - 1);
            return heights[x0, z0];
        }

        // -----------------------------
        // Update
        // -----------------------------
        public void UpdateAround(Vector3 worldPos, int _)
        {
            // --- Adaptive radii calculation ---
            float dt = _time.DeltaTime;
            if (dt <= 0f) dt = 1f / 60f;
            _avgDt = Lerp(_avgDt <= 0 ? dt : _avgDt, dt, 0.1f);

            float movedDistance = 0f;

            if (_hasLast)
            {
                float d = Vector3.Distance(worldPos, _lastCameraPos);
                movedDistance = d;
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
            // Terrain density via biome roughness/vegetation
            var biome = _biomeProvider.GetBiomeAt(new Vector2(worldPos.X, worldPos.Z), this);
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

            // During loading-screen warmup, keep radii deterministic and disable
            // adaptive thrash so progress can converge and complete cleanly.
            if (_isWarmupMode)
            {
                e = Clamp(baseEdit, _cfg.MinEditable, _cfg.MaxEditable);
                ro = Clamp(baseRO, _cfg.MinReadOnly, _cfg.MaxReadOnly);
                lod = Clamp(Math.Max(baseLOD, ro + 1), _cfg.MinLowLod, _cfg.MaxLowLod);
            }

            // Debounce: if camera hasn't moved perceptibly, keep previous radii to avoid thrashing
            if (_hasLast)
            {
                float dStill = movedDistance;
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
            _lastCameraPos = worldPos;
            _hasLast = true;

            // --- Predict forward position for preloading ---
            Vector3 predictedPos = worldPos;
            if (_lastMoveDirXZ != Vector3.Zero)
            {
                float lookAheadMeters = _speedMps * 1.0f; // 1 second lookahead
                predictedPos += _lastMoveDirXZ * lookAheadMeters;
            }

            // --- Update rings with computed radii ---
            // Keep ring centers anchored to the real camera position. Centering RO/LOD
            // on predicted positions can unload still-visible chunks (especially to the
            // sides/back) and create large distant holes while moving.
            _editableRing.UpdateAround(worldPos, e);
            _readOnlyRing.UpdateAround(worldPos, ro);

            // Stagger extra RO/LOD updates to distribute workload
            _frameCounter++;
            int roInterval = Math.Max(1, _cfg.ReadOnlyUpdateInterval);
            int lodInterval = Math.Max(1, roInterval * 2);

            if (!_isWarmupMode && _frameCounter % roInterval == 0)
            {
                // Lightweight ahead-of-motion top-up. We keep the center anchored to
                // worldPos for stable visibility and only expand radius for prefetch.
                _readOnlyRing.UpdateAround(predictedPos, ro + 1);
            }

            if (_lowLodRing is not null)
            {
                float roChunkWorld = _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
                float lodChunkWorld = _lowLodRing.ChunkSize * _lowLodRing.TileSize;
                int effectiveRo = ShouldShowReadOnlyRing() ? ro : 0;
                int lodInnerExclusion = (int)MathF.Ceiling((effectiveRo * roChunkWorld) / lodChunkWorld);
                _lowLodRing.InnerExclusionRadiusChunks = Math.Max(0, lodInnerExclusion);
                _lowLodRing.UpdateAround(worldPos, lod);
                if (!_isWarmupMode && _frameCounter % lodInterval == 0)
                    _lowLodRing.UpdateAround(predictedPos, lod + 1);
            }

            // --- Mesh generation ---
            int roUpdated = 0;
            foreach (var chunk in _readOnlyRing.GetLoadedChunks().Values)
            {
                if (roUpdated >= _cfg.MaxReadOnlyChunkUpdatesPerFrame) break;
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
                    roUpdated++;
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
                        roUpdated++;
                    }
                    else
                    {
                        // Fallback to full rebuild if we couldn't compute any patch rects
                        _renderer.MarkOriginDirty(chunk.Origin);
                        _renderer.EnqueueBuild(chunk.Heights, _readOnlyRing.TileSize, chunk.Origin);
                        chunk.IsMeshGenerated = true;
                        chunk.BuiltFromVersion = srcVer;
                        roUpdated++;
                    }
                }
            }

            int editableUpdated = 0;
            bool anyEditableDirty = false;
            foreach (var chunk in _editableRing.GetLoadedChunks().Values)
            {
                anyEditableDirty |= chunk.Dirty || !chunk.IsMeshGenerated;
                if (editableUpdated >= _cfg.MaxEditableRebuildsPerFrame) break;
                int srcVer = chunk.Version;
                if (!chunk.IsMeshGenerated || chunk.BuiltFromVersion != srcVer)
                {
                    _renderer.MarkOriginDirty(chunk.Origin);
                    _renderer.EnqueueBuild(chunk.Heights, _editableRing.TileSize, chunk.Origin);
                    chunk.IsMeshGenerated = true;
                    chunk.BuiltFromVersion = srcVer;
                    chunk.Dirty = false;
                    editableUpdated++;
                }
            }

            if (_lowLodRing is not null)
            {
                int lodUpdated = 0;
                foreach (var chunk in _lowLodRing.GetLoadedChunks().Values)
                {
                    if (lodUpdated >= _cfg.MaxLowLodChunkUpdatesPerFrame) break;
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
                        lodUpdated++;
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
                            lodUpdated++;
                        }
                        else
                        {
                            // Fallback to full rebuild
                            _renderer.MarkOriginDirty(chunk.Origin);
                            _renderer.EnqueueBuild(chunk.Heights, _lowLodRing.TileSize, chunk.Origin);
                            chunk.IsMeshGenerated = true;
                            chunk.BuiltFromVersion = srcVer;
                            lodUpdated++;
                        }
                    }
                }
            }

            // Upload a limited number of prepared meshes this frame.
            // During active editing, allow one extra upload to reduce visible dig latency.
            int uploadBudget = _cfg.MaxMeshBuildsPerFrame + (anyEditableDirty ? 1 : 0);
            _renderer.ProcessBuildQueue(Math.Max(1, uploadBudget));
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
        public void Render(CameraComponent camera)
        {
            bool showEditableRing = ShouldShowEditableRing();
            bool showReadOnlyRing = ShouldShowReadOnlyRing();
            bool showLowLodRing = ShouldShowLowLodRing();

            float roChunkWorld = _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
            float eChunkWorld = _editableRing.ChunkSize * _editableRing.TileSize;
            float lodChunkWorld = _lowLodRing is not null ? _lowLodRing.ChunkSize * _lowLodRing.TileSize : 1f;

            var roExclude = new HashSet<(int cx, int cz)>();
            var lodExclude = new HashSet<(int cx, int cz)>();
            // Always exclude areas covered by editable chunks from RO/LOD rendering.
            // If editable ring is hidden for debugging, nearby space should remain empty
            // instead of being filled by stale RO/LOD meshes.
            var editableChunks = _editableRing.GetLoadedChunks();
            var editableKeySet = new HashSet<(int cx, int cz)>(editableChunks.Keys);

            // Exclude RO and LOD chunks that overlap editable chunks.
            foreach (var (ecx, ecz) in editableKeySet)
            {
                if (!editableChunks.TryGetValue((ecx, ecz), out var editableChunk) || !editableChunk.IsMeshGenerated)
                    continue;

                float eMinX = ecx * eChunkWorld;
                float eMinZ = ecz * eChunkWorld;
                float eMaxX = eMinX + eChunkWorld;
                float eMaxZ = eMinZ + eChunkWorld;

                int roMinX = (int)MathF.Floor(eMinX / roChunkWorld);
                int roMaxX = (int)MathF.Floor((eMaxX - 1e-3f) / roChunkWorld);
                int roMinZ = (int)MathF.Floor(eMinZ / roChunkWorld);
                int roMaxZ = (int)MathF.Floor((eMaxZ - 1e-3f) / roChunkWorld);
                for (int cz = roMinZ; cz <= roMaxZ; cz++)
                for (int cx = roMinX; cx <= roMaxX; cx++)
                    roExclude.Add((cx, cz));

                if (_lowLodRing is not null)
                {
                    int lodMinX = (int)MathF.Floor(eMinX / lodChunkWorld);
                    int lodMaxX = (int)MathF.Floor((eMaxX - 1e-3f) / lodChunkWorld);
                    int lodMinZ = (int)MathF.Floor(eMinZ / lodChunkWorld);
                    int lodMaxZ = (int)MathF.Floor((eMaxZ - 1e-3f) / lodChunkWorld);
                    for (int cz = lodMinZ; cz <= lodMaxZ; cz++)
                    for (int cx = lodMinX; cx <= lodMaxX; cx++)
                        lodExclude.Add((cx, cz));
                }
            }

            // Exclude LOD chunks that overlap RO chunks only if the RO ring is actually being rendered.
            // This avoids far-distance holes when RO is intentionally hidden/disabled.
            if (_lowLodRing is not null)
            {
                if (showReadOnlyRing)
                {
                    var roChunks = _readOnlyRing.GetLoadedChunks();
                    foreach (var (rcx, rcz) in roChunks.Keys)
                    {
                        if (roExclude.Contains((rcx, rcz))) continue;
                        if (!roChunks.TryGetValue((rcx, rcz), out var roChunk) || !roChunk.IsMeshGenerated)
                            continue;
                        float rMinX = rcx * roChunkWorld;
                        float rMinZ = rcz * roChunkWorld;
                        float rMaxX = rMinX + roChunkWorld;
                        float rMaxZ = rMinZ + roChunkWorld;

                        int lodMinX = (int)MathF.Floor(rMinX / lodChunkWorld);
                        int lodMaxX = (int)MathF.Floor((rMaxX - 1e-3f) / lodChunkWorld);
                        int lodMinZ = (int)MathF.Floor(rMinZ / lodChunkWorld);
                        int lodMaxZ = (int)MathF.Floor((rMaxZ - 1e-3f) / lodChunkWorld);
                        for (int cz = lodMinZ; cz <= lodMaxZ; cz++)
                        for (int cx = lodMinX; cx <= lodMaxX; cx++)
                            lodExclude.Add((cx, cz));
                    }
                }
            }

            // Far
            if (showLowLodRing)
                _lowLodRing?.Render(camera, lodExclude);

            // Mid
            if (showReadOnlyRing)
                _readOnlyRing.RenderTiles(camera, roExclude);

            // Near
            if (showEditableRing)
                _editableRing.Render(camera);
        }

        // -----------------------------
        // Interface explicit implementations
        // -----------------------------
        void IInfiniteTerrain.RenderWithExclusions(CameraComponent camera, HashSet<(int cx, int cz)> exclude)
        {
            _readOnlyRing.RenderTiles(camera, exclude);
        }

        void IDebugTerrain.RenderDebugChunkBounds(CameraComponent camera)
        {
            _lowLodRing?.RenderDebugChunkBounds(camera);
            _readOnlyRing.RenderDebugChunkBounds(camera);
            _editableRing.RenderDebugChunkBounds(camera);
        }

        void IInfiniteTerrain.UpdateCenter(Vector3 cameraPosition) => UpdateAround(cameraPosition, 0);

        void IInfiniteTerrain.Update()
        {
            /* no-op */
        }

        void IInfiniteTerrain.Render(CameraComponent camera)
            => Render(camera);

        float IInfiniteTerrain.SampleHeight(Vector3 worldPos) => SampleHeight(worldPos);

        // -----------------------------
        // Editable terrain API
        // -----------------------------
        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine)
        {
            _editableRing.DigSphereAsync(worldCenter, radius, strength, falloff).GetAwaiter().GetResult();
            foreach (var chunk in _editableRing.GetLoadedChunks().Values)
            {
                if (chunk.Dirty || !chunk.IsMeshGenerated)
                {
                    _renderer.MarkOriginDirty(chunk.Origin);
                    _renderer.EnqueueBuild(chunk.Heights, _editableRing.TileSize, chunk.Origin);
                    chunk.IsMeshGenerated = true;
                    chunk.BuiltFromVersion = chunk.Version;
                    chunk.Dirty = false;
                }
            }
            _renderer.ProcessBuildQueue(Math.Max(1, _cfg.MaxMeshBuildsPerFrame + 2));
            return Task.CompletedTask;
        }

        public Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff)
            => _editableRing.PlaceSphereAsync(position, radius, strength, falloff);

        public bool TryMineAt(Vector3 position, float power, out ResourceBlockType blockType)
            => _editableRing.TryMineAt(position, power, out blockType);

        public TerrainDebugInfo GetDebugInfo(Vector3 worldPos)
        {
            float chunkWorld = ChunkSize * TileSize;
            int cx = (int)MathF.Floor(worldPos.X / chunkWorld);
            int cz = (int)MathF.Floor(worldPos.Z / chunkWorld);
            float modX = worldPos.X - cx * chunkWorld;
            float modZ = worldPos.Z - cz * chunkWorld;
            int localX = (int)MathF.Floor(modX / TileSize);
            int localZ = (int)MathF.Floor(modZ / TileSize);
            var biome = _biomeProvider.GetBiomeAt(new Vector2(worldPos.X, worldPos.Z), this);
            string biomeId = biome?.Data.DisplayName ?? "Unknown";
            return new TerrainDebugInfo(cx, cz, localX, localZ, ChunkSize, TileSize, biomeId, worldPos);
        }

        public async Task PumpAsyncJobs()
        {
            // Pump async edit jobs (editable ring)
            await _editableRing.PumpAsyncJobs();
            // Pump RO/LOD background height sampling jobs so chunks appear when ready
            if (_readOnlyRing is not null && _readOnlyRing is ReadOnlyTerrainService ro)
                await ro.PumpAsyncJobs(Math.Max(1, _cfg.MaxReadOnlyInstallsPerFrame));
            if (_lowLodRing is not null)
                await _lowLodRing.PumpAsyncJobs(Math.Max(1, _cfg.MaxLowLodInstallsPerFrame));
        }

        public void ProcessBuildQueueOnly()
        {
            _renderer.ProcessBuildQueue(Math.Max(1, _cfg.MaxMeshBuildsPerFrame));
        }

        public TerrainLoadingProgress GetLoadingProgress()
        {
            bool showReadOnlyRing = ShouldShowReadOnlyRing();
            bool showLowLodRing = ShouldShowLowLodRing();

            int desiredEditable = _editableRing.DesiredChunkCount;
            int desiredReadOnly = showReadOnlyRing ? _readOnlyRing.DesiredChunkCount : 0;
            int desiredLod = (_lowLodRing is not null && showLowLodRing) ? _lowLodRing.DesiredChunkCount : 0;

            int loadedEditable = _editableRing.LoadedChunkCount;
            int loadedReadOnly = showReadOnlyRing ? _readOnlyRing.LoadedChunkCount : 0;
            int loadedLod = (_lowLodRing is not null && showLowLodRing) ? _lowLodRing.LoadedChunkCount : 0;

            int generatingReadOnly = showReadOnlyRing ? _readOnlyRing.GeneratingChunkCount : 0;
            int generatingLod = (_lowLodRing is not null && showLowLodRing) ? _lowLodRing.GeneratingChunkCount : 0;
            int generatingTotal = generatingReadOnly + generatingLod;
            int loadedEntities = showReadOnlyRing ? _readOnlyRing.LoadedEntityCount : 0;

            int desiredTotal = desiredEditable + desiredReadOnly + desiredLod;
            int loadedTotal = loadedEditable + loadedReadOnly + loadedLod;

            float progress = desiredTotal > 0
                ? Math.Clamp(loadedTotal / (float)desiredTotal, 0f, 1f)
                : 1f;

            string stage;
            if (desiredTotal == 0) stage = "Preparing world";
            else if (loadedEditable < desiredEditable) stage = "Generating terrain: editable";
            else if (loadedReadOnly < desiredReadOnly || generatingReadOnly > 0) stage = "Generating terrain: read-only";
            else if (loadedLod < desiredLod || generatingLod > 0) stage = "Generating terrain: distant LOD";
            else if (loadedEntities <= 0) stage = "Adding entities and POIs";
            else if (generatingTotal > 0) stage = "Baking lighting and finishing";
            else stage = "Complete";

            return new TerrainLoadingProgress(progress, stage, desiredTotal, loadedTotal, generatingTotal, loadedEntities);
        }

        public void SetWarmupMode(bool enabled)
        {
            _isWarmupMode = enabled;
        }

        public IEnumerable<(Vector3 center, Vector3 size, Vector4 color)> EnumerateDebugChunkBounds()
        {
            foreach (var (center, size) in _lowLodRing?.EnumerateChunkBounds() ?? Array.Empty<(Vector3 center, Vector3 size)>())
                yield return (center, size, new Vector4(0.9f, 0.8f, 0.1f, 1f));

            foreach (var (center, size) in _readOnlyRing.EnumerateChunkBounds())
                yield return (center, size, new Vector4(0.2f, 0.6f, 1f, 1f));

            foreach (var (center, size) in _editableRing.EnumerateChunkBounds())
                yield return (center, size, new Vector4(0.1f, 0.9f, 0.1f, 1f));
        }

        private bool ShouldShowEditableRing()
            => !RuntimeEnvironment.IsDevelopmentEnvironment || _settings.Current.Debug.ShowEditableRing;

        private bool ShouldShowReadOnlyRing()
            => !RuntimeEnvironment.IsDevelopmentEnvironment || _settings.Current.Debug.ShowReadOnlyRing;

        private bool ShouldShowLowLodRing()
            => !RuntimeEnvironment.IsDevelopmentEnvironment || _settings.Current.Debug.ShowLowLodRing;
    }
}
