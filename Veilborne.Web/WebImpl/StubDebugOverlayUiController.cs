using Veilborne.Core.UI;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Ecs;
using Veilborne.Core.Sky;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Terrain;
using System.Numerics;
using System.Collections.Generic;

namespace Veilborne.Web.MonoGameImpl
{
    // Provide a stub implementation for DebugOverlayUiController dependencies
    public class StubDebugOverlayUiController : ITimeService, ISkyLightingService, IInfiniteTerrain, IDebugTerrain
    {
        // ITimeService
        public int Fps => 60;
        public int Ups => 60;
        public void Update(float dt) { }
        public float DeltaTime => 1f/60f;
        public void NotifyFrameRendered() { }
        public float TotalTime => 0;

        // ISkyLightingService
        public float TimeOfDay01 => 0;
        public float TimeOfDayHours24 => 12;
        public Vector3 SkyColor => Vector3.One;
        public Vector3 AmbientColor => Vector3.One;
        public Vector3 SunColor => Vector3.One;
        public Vector3 SunDirection => Vector3.UnitY;
        public float SunIntensity => 1;
        public float ShadowStrength => 1;

        // IInfiniteTerrain
        public void UpdateCenter(Vector3 v) { }
        public float SampleHeight(Vector3 v, float detailLevel = 1f) => 0;
        public void Update() { }
        public void Render(CameraComponent c) { }
        public void RenderWithExclusions(CameraComponent c, HashSet<(int cx, int cz)> exclusions) { }
        // IDebugTerrain
        public void RenderDebugChunkBounds(CameraComponent c) { }
        public TerrainDebugInfo GetDebugInfo(Vector3 v) => new(0, 0, 0, 0, 16, 1f, "StubBiome", v);
    }
}
