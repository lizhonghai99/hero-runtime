using Hero.Core.Observability;

namespace Hero.Core.Tests;

public sealed class SessionUsageStatsTests
{
    [Fact]
    public void Add_AccumulatesAndTracksLastContext()
    {
        var stats = new SessionUsageStats();
        var pricing = new TokenPricing
        {
            InputUsdPer1M = 1.0,
            OutputUsdPer1M = 1.0,
        };

        stats.Add(new TokenUsageDelta
        {
            InputTokens = 100,
            OutputTokens = 10,
            TotalTokens = 110,
            CachedInputTokens = 20,
        }, pricing);

        stats.Add(new TokenUsageDelta
        {
            InputTokens = 200,
            OutputTokens = 5,
            TotalTokens = 205,
            CachedInputTokens = 0,
        }, pricing);

        var snap = stats.Snapshot();
        Assert.Equal(300, snap.InputTokens);
        Assert.Equal(15, snap.OutputTokens);
        Assert.Equal(315, snap.TotalTokens);
        Assert.Equal(20, snap.CachedInputTokens);
        Assert.Equal(200, snap.LastContextTokens);
        Assert.Equal(0.000315, snap.EstimatedCostUsd, precision: 10);
    }

    [Fact]
    public void CacheHitPercent_ZeroInput_ReturnsNull()
    {
        var snap = new SessionUsageSnapshot
        {
            InputTokens = 0,
            CachedInputTokens = 0,
        };

        Assert.Null(snap.CacheHitPercent);
    }

    [Fact]
    public void CacheHitPercent_ComputesPercent()
    {
        var snap = new SessionUsageSnapshot
        {
            InputTokens = 200,
            CachedInputTokens = 50,
        };

        Assert.Equal(25.0, snap.CacheHitPercent);
    }

    [Fact]
    public void Add_ZeroInputUsesTotalAsLastContext()
    {
        var stats = new SessionUsageStats();
        stats.Add(new TokenUsageDelta
        {
            InputTokens = 0,
            OutputTokens = 0,
            TotalTokens = 77,
            CachedInputTokens = 0,
        });

        Assert.Equal(77, stats.LastContextTokens);
    }
}
