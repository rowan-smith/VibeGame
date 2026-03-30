using System.Numerics;

namespace Veilborne.Core.Sky
{
    public sealed class SkyLightingService : ISkyLightingService
    {
        private const float DayLengthSeconds = 1440f;
        private float _time01 = 0.32f;

        public float TimeOfDay01 => _time01;
        public float TimeOfDayHours24 => _time01 * 24f;
        public Vector3 SkyColor { get; private set; } = new(0.55f, 0.72f, 0.95f);
        public Vector3 AmbientColor { get; private set; } = new(0.38f, 0.40f, 0.44f);
        public Vector3 SunColor { get; private set; } = new(0.95f, 0.92f, 0.85f);
        public Vector3 SunDirection { get; private set; } = Vector3.Normalize(new Vector3(-0.35f, -1f, -0.25f));
        public float SunIntensity { get; private set; } = 1f;
        public float ShadowStrength { get; private set; } = 0.55f;

        public void Update(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
            {
                _time01 += deltaSeconds / DayLengthSeconds;
                _time01 -= MathF.Floor(_time01);
            }

            float sunAngle = (_time01 * MathF.PI * 2f) - MathF.PI * 0.5f;
            float sunY = MathF.Sin(sunAngle);
            float sunX = MathF.Cos(sunAngle);
            SunDirection = Vector3.Normalize(new Vector3(-sunX * 0.45f, -sunY, -0.3f));

            float daylight = Smooth01((sunY + 0.12f) / 1.12f);
            float dusk = 1f - MathF.Abs((_time01 - 0.75f) * 6f);
            dusk = Math.Clamp(dusk, 0f, 1f);

            Vector3 nightSky = new(0.03f, 0.05f, 0.11f);
            Vector3 daySky = new(0.50f, 0.70f, 0.95f);
            Vector3 duskTint = new(0.95f, 0.48f, 0.22f);
            SkyColor = Vector3.Lerp(Vector3.Lerp(nightSky, daySky, daylight), duskTint, dusk * (1f - daylight) * 0.45f);

            Vector3 nightAmbient = new(0.10f, 0.12f, 0.16f);
            Vector3 dayAmbient = new(0.42f, 0.44f, 0.47f);
            AmbientColor = Vector3.Lerp(nightAmbient, dayAmbient, daylight);

            Vector3 nightSun = new(0.25f, 0.32f, 0.42f);
            Vector3 daySun = new(0.98f, 0.94f, 0.86f);
            SunColor = Vector3.Lerp(nightSun, daySun, daylight);

            SunIntensity = 0.18f + (1.15f - 0.18f) * daylight;
            // Stronger, cleaner daytime shadows; soft at dusk/night.
            ShadowStrength = 0.20f + 0.70f * daylight;
        }

        private static float Smooth01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
