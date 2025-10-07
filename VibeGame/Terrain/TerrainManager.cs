using System.Numerics;
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
            // Drive rings with configured radii for stability
            _readOnlyRing.UpdateAround(worldPos, _cfg.ReadOnlyRadius);
            _editableRing.UpdateAround(worldPos, _cfg.EditableRadius);
            _lowLodRing?.UpdateAround(worldPos, _cfg.LowLodRadius);
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
            // Draw low-LOD backdrop beyond the read-only radius (if present)
            if (_lowLodRing is not null)
            {
                _lowLodRing.InnerExclusionRadiusChunks = _cfg.ReadOnlyRadius;
                _lowLodRing.Render(camera, baseColor);
            }

            // Draw read-only heightmap for mid-range
            _readOnlyRing.Render(camera, baseColor);

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
