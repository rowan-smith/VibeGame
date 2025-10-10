using System.Numerics;
using Raylib_CsLo;
using Veilborne.Core.Interfaces;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    /// <summary>
    /// Orchestrates ring-based terrain services (editable near, read-only mid, low LOD far).
    /// Implements IInfiniteTerrain and IEditableTerrain for compatibility.
    /// </summary>
    public class TerrainManager : IInfiniteTerrain, IEditableTerrain
    {
        private readonly EditableTerrainService _editableRing;
        private readonly ReadOnlyTerrainService _readOnlyRing;
        private readonly LowLodTerrainService? _lowLodRing;
        private readonly TerrainRingConfig _cfg;
        private readonly IBiomeProvider _biomeProvider;

        public TerrainManager(
            EditableTerrainService editableRing,
            ReadOnlyTerrainService readOnlyRing,
            TerrainRingConfig cfg,
            IBiomeProvider biomeProvider,
            LowLodTerrainService? lowLodRing = null)
        {
            _editableRing = editableRing;
            _readOnlyRing = readOnlyRing;
            _cfg = cfg;
            _lowLodRing = lowLodRing;
            _biomeProvider = biomeProvider;

            _editableRing.RenderBaseHeightmap = false; // read-only ring renders base
        }

        public float TileSize => _readOnlyRing.TileSize;
        public int ChunkSize => _readOnlyRing.ChunkSize;

        // -----------------------------
        // Update logic
        // -----------------------------
        public void UpdateAround(Vector3 worldPos, int _)
        {
            _readOnlyRing.UpdateAround(worldPos, _cfg.ReadOnlyRadius);
            _editableRing.UpdateAround(worldPos, _cfg.EditableRadius);

            if (_lowLodRing is not null)
            {
                int innerExcl = Math.Max(0, _cfg.ReadOnlyRadius - 2);
                _lowLodRing.InnerExclusionRadiusChunks = innerExcl;
                _lowLodRing.UpdateAround(worldPos, _cfg.LowLodRadius);
            }
        }

        // -----------------------------
        // Sample height / biome
        // -----------------------------
        public float SampleHeight(float worldX, float worldZ)
            => _editableRing.SampleHeight(worldX, worldZ);

        public float SampleHeight(Vector3 worldPos)
            => SampleHeight(worldPos.X, worldPos.Z);

        public IBiome GetBiomeAt(float worldX, float worldZ)
        {
            var adapter = new ReadOnlyTerrainAdapter(_readOnlyRing);
            return _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), adapter);
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
            => _readOnlyRing.GetNearbyObjectColliders(worldPos, range);

        // -----------------------------
        // Raylib rendering
        // -----------------------------
        public void Render(Camera3D camera, Color baseColor)
        {
            // Far ring first
            if (_lowLodRing is not null)
                _lowLodRing.Render(camera, baseColor);

            // Mid ring with exclusion around editable ring
            int excludeInner = Math.Max(0, _cfg.EditableRadius - 2);
            if (excludeInner > 0)
            {
                var exclude = new HashSet<(int cx, int cz)>();
                float chunkWorld = _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
                int ccx = (int)MathF.Floor(camera.position.X / chunkWorld);
                int ccz = (int)MathF.Floor(camera.position.Z / chunkWorld);
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
                _readOnlyRing.Render(camera, baseColor);
            }

            // Near ring last
            _editableRing.Render(camera, baseColor);
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            _readOnlyRing.RenderDebugChunkBounds(camera);
            _lowLodRing?.RenderDebugChunkBounds(camera);
            _editableRing.RenderDebugChunkBounds(camera);
        }

        public void RenderWithExclusions(Veilborne.Core.GameWorlds.Terrain.Camera camera, Color color, HashSet<(int cx, int cz)> exclude)
        {
            _readOnlyRing.RenderWithExclusions(camera.RaylibCamera, color, exclude);
        }

        // -----------------------------
        // Editable terrain API
        // -----------------------------
        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength = 1.0f, VoxelFalloff falloff = VoxelFalloff.Cosine)
            => _editableRing.DigSphereAsync(worldCenter, radius, strength, falloff);

        // PlaceSphereAsync: delegate to the editable ring
        public Task PlaceSphereAsync(Vector3 position, float radius, float strength, VoxelFalloff falloff)
        {
            return _editableRing.PlaceSphereAsync(position, radius, strength, falloff);
        }

        // RenderDebugChunkBounds: call debug render on all rings
        public void RenderDebugChunkBounds(Veilborne.Core.GameWorlds.Terrain.Camera camera)
        {
            _lowLodRing?.RenderDebugChunkBounds(camera.RaylibCamera);
            _readOnlyRing.RenderDebugChunkBounds(camera.RaylibCamera);
            _editableRing.RenderDebugChunkBounds(camera.RaylibCamera);
        }

        // Provide debug information about current chunk/biome
        public TerrainDebugInfo GetDebugInfo(Vector3 worldPos)
        {
            float chunkWorld = ChunkSize * TileSize;
            int cx = (int)MathF.Floor(worldPos.X / chunkWorld);
            int cz = (int)MathF.Floor(worldPos.Z / chunkWorld);

            // Local coordinates within chunk in tile units
            float modX = worldPos.X - MathF.Floor(worldPos.X / chunkWorld) * chunkWorld;
            float modZ = worldPos.Z - MathF.Floor(worldPos.Z / chunkWorld) * chunkWorld;
            int localX = (int)MathF.Floor(modX / TileSize);
            int localZ = (int)MathF.Floor(modZ / TileSize);

            var biome = GetBiomeAt(worldPos.X, worldPos.Z);
            string biomeId = biome?.Data.DisplayName ?? "Unknown";
            return new TerrainDebugInfo(cx, cz, localX, localZ, ChunkSize, TileSize, biomeId, worldPos);
        }

        public Task PumpAsyncJobs() => _editableRing.PumpAsyncJobs();

        // -----------------------------
        // IInfiniteTerrain interface
        // -----------------------------
        void IInfiniteTerrain.UpdateCenter(Vector3 cameraPosition) => UpdateAround(cameraPosition, 0);

        float IInfiniteTerrain.SampleHeight(Vector3 worldPos) => SampleHeight(worldPos);

        void IInfiniteTerrain.Update() { /* no-op */ }

        void IInfiniteTerrain.Render(Veilborne.Core.GameWorlds.Terrain.Camera camera, Color color) => Render(camera.RaylibCamera, color);

        void IInfiniteTerrain.RenderWithExclusions(Veilborne.Core.GameWorlds.Terrain.Camera camera, Color color, HashSet<(int cx, int cz)> exclude)
            => RenderWithExclusions(camera, color, exclude);
    }
}
