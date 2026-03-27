using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Sky
{
    /// <summary>
    /// CPU directional shadow map approximation in light-space for terrain + world object casters.
    /// Provides stable world-position shadow sampling for renderers.
    /// </summary>
    public sealed class CpuShadowMapService : IShadowMapService
    {
        private readonly ISkyLightingService _sky;
        private readonly IInfiniteTerrain _terrain;
        private readonly EntityRegistry _entities;

        private const int MapSize = 96;
        private const float WorldExtent = 140f;
        private const float RebuildIntervalSeconds = 0.10f;
        private const float CameraRecenterThresholdSq = 9f; // 3m
        private const float SunDirRebuildDotThreshold = 0.9994f;
        private const float MaxCasterDistanceSq = 180f * 180f;
        private readonly float[] _heightMap = new float[MapSize * MapSize];
        private bool _ready;
        private Vector2 _mapCenterXZ;
        private (Vector3 right, Vector3 up, Vector3 forward) _basis;
        private Vector3 _cachedLightDir = Vector3.UnitY;
        private Vector3 _centerLightPos;
        private float _rebuildTimer;

        public bool IsReady => _ready;

        public CpuShadowMapService(ISkyLightingService sky, IInfiniteTerrain terrain, EntityRegistry entities)
        {
            _sky = sky;
            _terrain = terrain;
            _entities = entities;
        }

        public void Update(float deltaSeconds)
        {
            _rebuildTimer += MathF.Max(0f, deltaSeconds);
            var nextCenter = GetActiveCameraCenter();
            Vector3 lightDir = Vector3.Normalize(-_sky.SunDirection);
            bool needsRebuild = !_ready ||
                                _rebuildTimer >= RebuildIntervalSeconds ||
                                Vector2.DistanceSquared(nextCenter, _mapCenterXZ) >= CameraRecenterThresholdSq ||
                                Vector3.Dot(lightDir, _cachedLightDir) < SunDirRebuildDotThreshold;
            if (!needsRebuild) return;

            _rebuildTimer = 0f;
            _mapCenterXZ = nextCenter;
            _cachedLightDir = lightDir;
            _basis = BuildLightBasis(lightDir);
            _centerLightPos = WorldToLight(new Vector3(_mapCenterXZ.X, 0f, _mapCenterXZ.Y), _basis);

            Array.Fill(_heightMap, float.NegativeInfinity);
            float step = (WorldExtent * 2f) / (MapSize - 1);

            // Terrain caster heights.
            for (int z = 0; z < MapSize; z++)
            for (int x = 0; x < MapSize; x++)
            {
                float wx = _mapCenterXZ.X - WorldExtent + x * step;
                float wz = _mapCenterXZ.Y - WorldExtent + z * step;
                float wy = _terrain.SampleHeight(new Vector3(wx, 0f, wz));
                var lightPos = WorldToLight(new Vector3(wx, wy, wz), _basis);
                WriteHeight(lightPos);
            }

            // World objects as additional casters.
            _entities.ForEachWith<TransformComponent, ShadowCasterComponent>(entity =>
            {
                var caster = entity.GetComponent<ShadowCasterComponent>();
                if (!caster.CastsShadows) return;
                var tr = entity.GetComponent<TransformComponent>();
                if (Vector2.DistanceSquared(new Vector2(tr.Position.X, tr.Position.Z), _mapCenterXZ) > MaxCasterDistanceSq)
                    return;
                float radius = MathF.Max(0.2f, MathF.Max(MathF.Abs(tr.Scale.X), MathF.Max(MathF.Abs(tr.Scale.Y), MathF.Abs(tr.Scale.Z))));
                // Sample top of object for conservative occluder height.
                var top = tr.Position + new Vector3(0f, radius * 2f, 0f);
                var lp = WorldToLight(top, _basis);
                WriteHeight(lp);
            });

            _ready = true;
        }

        public float SampleShadow(Vector3 worldPosition)
        {
            if (!_ready) return 1f;

            var lp = WorldToLight(worldPosition, _basis);
            if (!TryGetCell(lp, out int ix, out int iz)) return 1f;

            // 2x2 PCF to reduce cost while keeping acceptable softness.
            float occlusion = 0f;
            int samples = 0;
            for (int dz = 0; dz <= 1; dz++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int sx = ix + dx;
                int sz = iz + dz;
                if (sx < 0 || sz < 0 || sx >= MapSize || sz >= MapSize) continue;
                float mapH = _heightMap[sz * MapSize + sx];
                if (!float.IsFinite(mapH)) continue;
                // Bias reduces terrain self-shadowing (fixes "chunk goes dark on dig").
                float bias = 1.8f;
                if (lp.Y <= mapH + bias) occlusion += 1f;
                samples++;
            }
            if (samples == 0) return 1f;
            float occ = occlusion / samples;
            return 1f - occ * 0.50f;
        }

        private void WriteHeight(Vector3 lightPos)
        {
            if (!TryGetCell(lightPos, out int ix, out int iz)) return;
            int idx = iz * MapSize + ix;
            if (lightPos.Y > _heightMap[idx]) _heightMap[idx] = lightPos.Y;
        }

        private bool TryGetCell(Vector3 lightPos, out int ix, out int iz)
        {
            float u = (lightPos.X - _centerLightPos.X + WorldExtent) / (WorldExtent * 2f);
            float v = (lightPos.Z - _centerLightPos.Z + WorldExtent) / (WorldExtent * 2f);
            ix = (int)MathF.Floor(u * (MapSize - 1));
            iz = (int)MathF.Floor(v * (MapSize - 1));
            return ix >= 0 && iz >= 0 && ix < MapSize && iz < MapSize;
        }

        private Vector2 GetActiveCameraCenter()
        {
            Vector2 center = Vector2.Zero;
            bool found = false;
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                if (found) return;
                var cam = entity.GetComponent<CameraComponent>();
                center = new Vector2(cam.Position.X, cam.Position.Z);
                found = true;
            });
            return center;
        }

        private static (Vector3 right, Vector3 up, Vector3 forward) BuildLightBasis(Vector3 forward)
        {
            Vector3 aux = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(aux, forward));
            Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
            return (right, up, forward);
        }

        private static Vector3 WorldToLight(Vector3 world, (Vector3 right, Vector3 up, Vector3 forward) b)
        {
            return new Vector3(
                Vector3.Dot(world, b.right),
                Vector3.Dot(world, b.up),
                Vector3.Dot(world, b.forward));
        }
    }
}
