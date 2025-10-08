using System.Numerics;
using System.Collections.Generic;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    // Orchestrates ring-based terrain services (editable near, read-only mid, low LOD far).
    // Implements existing IInfiniteTerrain contract for compatibility.
    public class TerrainManager : IInfiniteTerrain, IEditableTerrain
    {
        private readonly EditableTerrainService _editableRing;     // ring 1
        private readonly ReadOnlyTerrainService _readOnlyRing;     // ring 2+
        private readonly LowLodTerrainService? _lowLodRing;       // ring 3+ (optional)
        private readonly TerrainRingConfig _cfg;

        public TerrainManager(EditableTerrainService editableRing,
                              ReadOnlyTerrainService readOnlyRing,
                              TerrainRingConfig cfg,
                              LowLodTerrainService? lowLodRing = null)
        {
            _editableRing = editableRing;
            _readOnlyRing = readOnlyRing;
            _cfg = cfg;
            _lowLodRing = lowLodRing;

            // Let read-only heightmap render the base; editable ring overlays voxel surfaces only
            _editableRing.RenderBaseHeightmap = false;
        }

        public float TileSize => _readOnlyRing.TileSize; // single source of truth
        public int ChunkSize => _readOnlyRing.ChunkSize;

        public void UpdateAround(Vector3 worldPos, int _)
        {
            // Drive rings with configured radii and set inner exclusions to create 1-chunk overlaps
            _readOnlyRing.UpdateAround(worldPos, _cfg.ReadOnlyRadius);
            _editableRing.UpdateAround(worldPos, _cfg.EditableRadius);

            if (_lowLodRing is not null)
            {
                // Low LOD should overlap the read-only ring by 1 chunk: start at (ReadOnlyRadius - 1)
                // Exclude strictly inside that start ⇒ exclude radius = (ReadOnlyRadius - 2)
                int innerExcl = Math.Max(0, _cfg.ReadOnlyRadius - 2);
                _lowLodRing.InnerExclusionRadiusChunks = innerExcl;
                _lowLodRing.UpdateAround(worldPos, _cfg.LowLodRadius);
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // Prefer editable voxel surface when available; it internally falls back to heightmap
            return _editableRing.SampleHeight(worldX, worldZ);
        }

        public IBiome GetBiomeAt(float worldX, float worldZ)
        {
            // Biome provider is owned by the heightmap service
            return _readOnlyRing.GetBiomeAt(worldX, worldZ);
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            // Static world objects are attached to the heightmap ring
            return _readOnlyRing.GetNearbyObjectColliders(worldPos, range);
        }

        public void Render(Camera3D camera, Color baseColor)
        {
            // Draw low-LOD backdrop, excluding inner area so it overlaps the read-only ring by 1 chunk
            if (_lowLodRing is not null)
            {
                int innerExcl = Math.Max(0, _cfg.ReadOnlyRadius - 2); // LowLOD starts at (ReadOnlyRadius - 1)
                _lowLodRing.InnerExclusionRadiusChunks = innerExcl;
                _lowLodRing.Render(camera, baseColor);
            }

            // Compute exclusion set for the read-only ring so it doesn't draw the very inner core
            int excludeInner = Math.Max(0, _cfg.EditableRadius - 2); // ReadOnly starts at (EditableRadius - 1)
            if (excludeInner > 0)
            {
                float chunkWorld = (_readOnlyRing.ChunkSize - 1) * _readOnlyRing.TileSize;
                int ccx = (int)MathF.Floor(camera.position.X / chunkWorld);
                int ccz = (int)MathF.Floor(camera.position.Z / chunkWorld);
                var exclude = new HashSet<(int cx, int cz)>();
                for (int dz = -_cfg.ReadOnlyRadius; dz <= _cfg.ReadOnlyRadius; dz++)
                {
                    for (int dx = -_cfg.ReadOnlyRadius; dx <= _cfg.ReadOnlyRadius; dx++)
                    {
                        int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
                        if (ring <= excludeInner)
                            exclude.Add((ccx + dx, ccz + dz));
                    }
                }
                _readOnlyRing.RenderWithExclusions(camera, baseColor, exclude);
            }
            else
            {
                // No exclusion needed
                _readOnlyRing.Render(camera, baseColor);
            }

            // Overlay local editable voxel surfaces (base heightmap suppressed inside the editable ring)
            _editableRing.Render(camera, baseColor);
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            _readOnlyRing.RenderDebugChunkBounds(camera);
            _lowLodRing?.RenderDebugChunkBounds(camera);
            _editableRing.RenderDebugChunkBounds(camera);
        }

        // IEditableTerrain implementation (for brush edits)
        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine)
        {
            return _editableRing.DigSphereAsync(worldCenter, radius, strength, falloff);
        }

        public void PumpAsyncJobs()
        {
            _editableRing.PumpAsyncJobs();
        }
    }
}
