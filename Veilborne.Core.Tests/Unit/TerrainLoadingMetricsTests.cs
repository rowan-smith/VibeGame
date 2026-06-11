using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Unit;

public class TerrainLoadingMetricsTests
{
    [Fact]
    public void ComputeProgress_moves_when_only_generating_count_drops()
    {
        float at293 = TerrainLoadingMetrics.ComputeProgress01(314, 293, 21, 81, 68, 0);
        float at300 = TerrainLoadingMetrics.ComputeProgress01(314, 300, 14, 81, 72, 0);

        Assert.InRange(at293, 0.90f, 0.96f);
        Assert.True(at300 > at293);
    }

    [Fact]
    public void ComputeProgress_reaches_one_when_playable_and_spawns_complete()
    {
        float done = TerrainLoadingMetrics.ComputeProgress01(314, 314, 0, 81, 81, 0);
        Assert.Equal(1f, done);
    }

    [Fact]
    public void ComputeProgress_creeps_with_background_lod_while_playable_tail_stalls()
    {
        float earlyLod = TerrainLoadingMetrics.ComputeProgress01(314, 293, 21, 81, 68, 0);
        float laterLod = TerrainLoadingMetrics.ComputeProgress01(314, 293, 21, 81, 75, 0);

        Assert.True(laterLod > earlyLod);
    }
}
