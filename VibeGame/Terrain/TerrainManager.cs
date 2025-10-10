using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_CsLo;
using Veilborne.Core.GameWorlds.Terrain;
using Veilborne.Core.Interfaces;
using VibeGame.Biomes;
using VibeGame.Interfaces;

namespace VibeGame.Terrain
{
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
        }

        public float TileSize => _readOnlyRing.TileSize;
        public int ChunkSize => _readOnlyRing.ChunkSize;

        // -----------------------------
        // Update
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
            int excludeInner = Math.Max(0, _cfg.EditableRadius - 2);
            if (excludeInner > 0)
            {
                float chunkWorld = _readOnlyRing.ChunkSize * _readOnlyRing.TileSize;
                int ccx = (int)MathF.Floor(camera.position.X / chunkWorld);
                int ccz = (int)MathF.Floor(camera.position.Z / chunkWorld);
                for (int dz = -_cfg.ReadOnlyRadius; dz <= _cfg.ReadOnlyRadius; dz++)
                for (int dx = -_cfg.ReadOnlyRadius; dx <= _cfg.ReadOnlyRadius; dx++)
                {
                    int ring = Math.Max(Math.Abs(dx), Math.Abs(dz));
                    if (ring <= excludeInner) exclude.Add((ccx + dx, ccz + dz));
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

        public Task PumpAsyncJobs() => _editableRing.PumpAsyncJobs();
    }
}
