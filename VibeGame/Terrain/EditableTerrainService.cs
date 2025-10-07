using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    // Hybrid terrain: heightmap surface + local editable voxel chunks
    public class EditableTerrainService : IInfiniteTerrain, IEditableTerrain
    {
        private readonly ReadOnlyTerrainService _heightmap;
        private readonly int _voxelChunkSize;
        private readonly float _voxelSize;

        // 3D voxel chunks keyed by (cx, cy, cz)
        private readonly Dictionary<(int cx, int cy, int cz), VoxelChunk> _voxels = new();

        // Simple background job queue placeholder
        private readonly Queue<VoxelChunk> _rebuildQueue = new();

        // Settings
        private readonly int _maxActiveVoxelChunks = 128;
        private int _renderRadiusChunks = 2;

        // Track current center heightmap chunk for ring calculations
        private int _centerChunkX;
        private int _centerChunkZ;

        public EditableTerrainService(ReadOnlyTerrainService heightmap)
        {
            _heightmap = heightmap;
            _voxelChunkSize = 32;
            // Use finer local voxels so edits are clearly visible (half of tile size)
            _voxelSize = MathF.Max(0.25f, heightmap.TileSize * 0.5f);
        }

        public float TileSize => _heightmap.TileSize;
        public int ChunkSize => _heightmap.ChunkSize;

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            _renderRadiusChunks = radiusChunks;
            _heightmap.UpdateAround(worldPos, radiusChunks);

            // Update current center heightmap chunk for ring calculations
            float hmChunkWorld = (_heightmap.ChunkSize - 1) * _heightmap.TileSize;
            _centerChunkX = (int)MathF.Floor(worldPos.X / hmChunkWorld);
            _centerChunkZ = (int)MathF.Floor(worldPos.Z / hmChunkWorld);

            // Ensure a small grid of voxel chunks is available around the player at ground level
            float span = _voxelChunkSize * _voxelSize;
            // Snap Y to ground voxel layer so edits are near the terrain surface
            float groundY = _heightmap.SampleHeight(worldPos.X, worldPos.Z);
            int cy = (int)MathF.Floor(groundY / span);
            // Keep voxel bubble very tight for performance (visual overlay only near player)
            int voxRad = 1; // fixed near-ring overlay only
            var baseKey = WorldToVoxelChunk(new Vector3(worldPos.X, cy * span, worldPos.Z));
            for (int dz = -voxRad; dz <= voxRad; dz++)
            for (int dx = -voxRad; dx <= voxRad; dx++)
            {
                var key = (baseKey.cx + dx, cy, baseKey.cz + dz);
                _ = GetOrCreateChunk(key);
            }

            // LOD assignment for existing voxel chunks using ring distances from center heightmap chunk
            foreach (var kv in _voxels.ToArray())
            {
                var chunk = kv.Value;
                Vector3 center = chunk.OriginWorld + new Vector3(chunk.Size * chunk.VoxelSize * 0.5f);
                int ring = GetRingForWorld(center);
                int lod = ring <= 1 ? 0 : (ring <= 3 ? 1 : 2);
                if (chunk.LodLevel != lod)
                {
                    chunk.LodLevel = lod;
                    chunk.MarkDirty();
                    EnqueueRebuild(chunk);
                }
            }

            // Remove far chunks outside a small safety radius to keep dictionary tiny
            foreach (var kv in _voxels.ToArray())
            {
                var ch = kv.Value;
                Vector3 c = ch.OriginWorld + new Vector3(ch.Size * ch.VoxelSize * 0.5f);
                if (GetRingForWorld(c) > 2)
                    _voxels.Remove(kv.Key);
            }

            // Evict farthest chunks if somehow still exceeding cap
            if (_voxels.Count > _maxActiveVoxelChunks)
            {
                var ordered = _voxels.OrderByDescending(p => Vector3.Distance(worldPos, p.Value.OriginWorld));
                foreach (var kvp in ordered.Skip(_maxActiveVoxelChunks))
                    _voxels.Remove(kvp.Key);
            }

            PumpAsyncJobs();
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // Base height from the rendered heightmap (already includes biome overlay)
            float h = _heightmap.SampleHeight(worldX, worldZ);

            // If a voxel chunk exists at this XZ around the ground level, use its top solid voxel
            float span = _voxelChunkSize * _voxelSize;
            int cy = (int)MathF.Floor(h / span);
            var key = WorldToVoxelChunk(new Vector3(worldX, cy * span, worldZ));
            if (_voxels.TryGetValue(key, out var chunk))
            {
                int ix = (int)Math.Clamp(MathF.Floor((worldX - chunk.OriginWorld.X) / _voxelSize), 0f, chunk.Size - 1f);
                int iz = (int)Math.Clamp(MathF.Floor((worldZ - chunk.OriginWorld.Z) / _voxelSize), 0f, chunk.Size - 1f);
                for (int y = chunk.Size - 1; y >= 0; y--)
                {
                    if (chunk.GetDensity(ix, y, iz) > 0f)
                    {
                        float vy = chunk.OriginWorld.Y + (y + 1) * chunk.VoxelSize;
                        if (vy > h) h = vy;
                        break;
                    }
                }
            }

            return h;
        }

        public IBiome GetBiomeAt(float worldX, float worldZ) => _heightmap.GetBiomeAt(worldX, worldZ);

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            return _heightmap.GetNearbyObjectColliders(worldPos, range);
        }

        public bool RenderBaseHeightmap { get; set; } = true;

        public void Render(Camera3D camera, Color baseColor)
        {
            // Optionally draw the full heightmap terrain first; we'll overlay voxel surfaces slightly above it.
            // This avoids visible holes along chunk borders when voxel chunks are present.
            if (RenderBaseHeightmap)
            {
                _heightmap.Render(camera, baseColor);
            }

            // Overlay voxel chunk surfaces (semi-transparent) to visualize edits
            foreach (var v in _voxels.Values)
            {
                // Quick ring/distance culling: only draw very near chunks
                Vector3 center = v.OriginWorld + new Vector3(v.Size * v.VoxelSize * 0.5f);
                int ring = GetRingForWorld(center);
                if (ring > 1) continue; // overlay only in immediate ring

                // Simple distance culling relative to camera
                float dx = center.X - camera.position.X;
                float dz = center.Z - camera.position.Z;
                float maxDist = (_heightmap.ChunkSize - 1) * _heightmap.TileSize * 1.5f; // within ~1.5 chunks
                if ((dx * dx + dz * dz) > maxDist * maxDist) continue;

                // Biome-tinted color with alpha varying by LOD
                var biome = _heightmap.GetBiomeAt(center.X, center.Z);
                var bc = biome.Data.ColorPalette.Primary;
                float lm = MathF.Max(0.2f, biome.Data.LightingModifier);
                byte r = (byte)Math.Clamp((int)(bc.R * lm), 0, 255);
                byte g = (byte)Math.Clamp((int)(bc.G * lm), 0, 255);
                byte b = (byte)Math.Clamp((int)(bc.B * lm), 0, 255);
                byte a = v.LodLevel == 0 ? (byte)210 : v.LodLevel == 1 ? (byte)170 : (byte)130;
                Color c = new Color(r, g, b, a);
                v.RenderSurfaceCubes(c);
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            // Draw heightmap chunk bounds first
            _heightmap.RenderDebugChunkBounds(camera);

            // Then overlay voxel chunk bounds with LOD-colored wires
            foreach (var v in _voxels.Values)
            {
                // Only draw debug bounds for near chunks to avoid excessive line drawing
                Vector3 center = v.OriginWorld + new Vector3(v.Size * v.VoxelSize * 0.5f);
                if (GetRingForWorld(center) > 1) continue;
                Color c = v.LodLevel == 0 ? new Color(0, 220, 255, 220)
                                          : (v.LodLevel == 1 ? new Color(140, 160, 255, 220) : new Color(180, 120, 255, 220));
                v.RenderDebugBounds(c);
            }
        }

        public async Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine)
        {
            // Respect ring editability: rings 0–1 fully editable; 2–3 semi-editable; 4+ read-only
            int ringHere = GetRingForWorld(worldCenter);
            if (ringHere >= 4)
            {
                // Read-only region: ignore edit
                await Task.Yield();
                return;
            }

            // Ensure voxel chunks exist covering the brush volume
            var keys = GetOverlappingVoxelKeys(worldCenter, radius);
            foreach (var key in keys)
            {
                var chunk = GetOrCreateChunk(key);
                VoxelBrush.ApplySphereToChunk(chunk, worldCenter, radius, strength, falloff);
                EnqueueRebuild(chunk);
            }

            // Simulate async behavior
            await Task.Yield();
        }

        public void PumpAsyncJobs()
        {
            // Build one chunk per frame for responsiveness (placeholder)
            if (_rebuildQueue.Count == 0) return;
            var next = _rebuildQueue.Dequeue();
            next.EnqueueRebuild();
        }

        private void EnqueueRebuild(VoxelChunk chunk)
        {
            _rebuildQueue.Enqueue(chunk);
            if (_rebuildQueue.Count > 256)
            {
                // drop extras
                _rebuildQueue.Dequeue();
            }
        }

        private (int cx, int cy, int cz) WorldToVoxelChunk(Vector3 p)
        {
            float span = _voxelChunkSize * _voxelSize;
            int cx = (int)MathF.Floor(p.X / span);
            int cy = (int)MathF.Floor(p.Y / span);
            int cz = (int)MathF.Floor(p.Z / span);
            return (cx, cy, cz);
        }

        private IEnumerable<(int cx, int cy, int cz)> GetOverlappingVoxelKeys(Vector3 center, float radius)
        {
            float span = _voxelChunkSize * _voxelSize;
            Vector3 min = center - new Vector3(radius);
            Vector3 max = center + new Vector3(radius);
            int cx0 = (int)MathF.Floor(min.X / span);
            int cy0 = (int)MathF.Floor(min.Y / span);
            int cz0 = (int)MathF.Floor(min.Z / span);
            int cx1 = (int)MathF.Floor(max.X / span);
            int cy1 = (int)MathF.Floor(max.Y / span);
            int cz1 = (int)MathF.Floor(max.Z / span);
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                yield return (cx, cy, cz);
        }

        private int GetRingForWorld(Vector3 p)
        {
            float hmChunkWorld = (_heightmap.ChunkSize - 1) * _heightmap.TileSize;
            int cx = (int)MathF.Floor(p.X / hmChunkWorld);
            int cz = (int)MathF.Floor(p.Z / hmChunkWorld);
            int dx = Math.Abs(cx - _centerChunkX);
            int dz = Math.Abs(cz - _centerChunkZ);
            return Math.Max(dx, dz);
        }

        private VoxelChunk GetOrCreateChunk((int cx, int cy, int cz) key)
        {
            if (_voxels.TryGetValue(key, out var existing))
                return existing;

            float span = _voxelChunkSize * _voxelSize;
            Vector3 origin = new Vector3(key.cx * span, key.cy * span, key.cz * span);
            var chunk = new VoxelChunk(origin, _voxelChunkSize, _voxelSize);

            // Initialize densities from heightmap so the surface matches before edits
            InitializeFromHeightmap(chunk);

            _voxels[key] = chunk;
            return chunk;
        }

        private void InitializeFromHeightmap(VoxelChunk chunk)
        {
            int n = chunk.Size;
            float vs = chunk.VoxelSize;
            Vector3 origin = chunk.OriginWorld;
            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float wx = origin.X + (x + 0.5f) * vs;
                float wy = origin.Y + (y + 0.5f) * vs;
                float wz = origin.Z + (z + 0.5f) * vs;
                float surfaceY = _heightmap.SampleHeight(wx, wz);
                // Positive => solid below surface, negative => empty above
                float density = surfaceY - wy;
                chunk.SetDensity(x, y, z, density);
            }
            chunk.MarkDirty();
            EnqueueRebuild(chunk);
        }
    }
}
