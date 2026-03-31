using System.Numerics;
using Veilborne.Core.Sky;

namespace Veilborne.Web.MonoGameImpl
{
    public class WebSkyLightingService : ISkyLightingService
    {
        private readonly SkyLightingService _impl = new SkyLightingService();
        public float TimeOfDay01 => _impl.TimeOfDay01;
        public float TimeOfDayHours24 => _impl.TimeOfDayHours24;
        public Vector3 SkyColor => _impl.SkyColor;
        public Vector3 AmbientColor => _impl.AmbientColor;
        public Vector3 SunColor => _impl.SunColor;
        public Vector3 SunDirection => _impl.SunDirection;
        public float SunIntensity => _impl.SunIntensity;
        public float ShadowStrength => _impl.ShadowStrength;
        public void Update(float deltaSeconds) => _impl.Update(deltaSeconds);
    }
}

