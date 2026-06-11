using System.Numerics;

namespace Veilborne.UI
{
    /// <summary>
    /// Colors ECS hotspot lines relative to the current frame budget so steady 60 FPS
    /// does not show false-critical red when a system uses ~20% of the frame.
    /// </summary>
    public static class PerformanceHotspotColor
    {
        public const double DefaultFrameBudgetMs = 1000d / 60d;

        public static double ResolveFrameBudgetMs(int fps) =>
            fps > 0 ? 1000d / fps : DefaultFrameBudgetMs;

        public static Vector4 ForSystemTotalMs(double totalMs, double frameBudgetMs)
        {
            frameBudgetMs = frameBudgetMs > 0d ? frameBudgetMs : DefaultFrameBudgetMs;
            double pct = totalMs / frameBudgetMs;
            if (pct > 0.40d)
                return new Vector4(1f, 0.4f, 0.4f, 1f);
            if (pct > 0.18d)
                return new Vector4(0.92f, 0.82f, 0.45f, 1f);
            return new Vector4(0.6f, 0.8f, 0.6f, 1f);
        }

        public static bool IsStalePeak(double peakMs, double avgMs) =>
            avgMs > 0.01d && peakMs > Math.Max(8d, avgMs * 4d);
    }
}
