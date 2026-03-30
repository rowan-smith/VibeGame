using System.Numerics;

namespace Veilborne.Core.Sky
{
    public interface ISkyLightingService
    {
        float TimeOfDay01 { get; }
        float TimeOfDayHours24 { get; }
        Vector3 SkyColor { get; }
        Vector3 AmbientColor { get; }
        Vector3 SunColor { get; }
        Vector3 SunDirection { get; }
        float SunIntensity { get; }
        float ShadowStrength { get; }
        void Update(float deltaSeconds);
    }
}
