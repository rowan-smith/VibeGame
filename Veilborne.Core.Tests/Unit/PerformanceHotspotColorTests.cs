using Veilborne.UI;

namespace Veilborne.Core.Tests.Unit;

public class PerformanceHotspotColorTests
{
    [Theory]
    [InlineData(3.8, 60, false)] // ~23% of 16.7ms budget — yellow, not red
    [InlineData(7.0, 60, true)]  // ~42% — red
    [InlineData(2.0, 60, false)] // ~12% — green
    public void ForSystemTotalMs_uses_frame_budget_not_fixed_threshold(double totalMs, int fps, bool expectRed)
    {
        double budget = PerformanceHotspotColor.ResolveFrameBudgetMs(fps);
        var color = PerformanceHotspotColor.ForSystemTotalMs(totalMs, budget);
        bool isRed = color.X > 0.95f && color.Y < 0.5f;
        Assert.Equal(expectRed, isRed);
    }

    [Fact]
    public void IsStalePeak_flags_loading_spikes_above_running_average()
    {
        Assert.True(PerformanceHotspotColor.IsStalePeak(peakMs: 82d, avgMs: 3.8d));
        Assert.False(PerformanceHotspotColor.IsStalePeak(peakMs: 6d, avgMs: 3.8d));
    }
}
