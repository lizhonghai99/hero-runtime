using Hero.Core.Observability;

namespace Hero.Core.Tests;

public sealed class TokenPricingTests
{
    [Fact]
    public void EstimateUsd_BasicInputAndOutput()
    {
        var pricing = new TokenPricing
        {
            InputUsdPer1M = 1.0,
            OutputUsdPer1M = 1.0,
            CachedInputUsdPer1M = null,
        };

        var cost = pricing.EstimateUsd(Delta(1000, 100, 0));

        Assert.Equal(0.0011, cost, precision: 10);
    }

    [Fact]
    public void EstimateUsd_CachedUsesSeparateRate_AndStripsFromInput()
    {
        var pricing = new TokenPricing
        {
            InputUsdPer1M = 1.0,
            OutputUsdPer1M = 0,
            CachedInputUsdPer1M = 0.1,
        };

        var cost = pricing.EstimateUsd(Delta(1000, 0, 400));

        // (1000-400)/1e6 * 1.0 + 400/1e6 * 0.1
        Assert.Equal(0.00064, cost, precision: 10);
    }

    [Fact]
    public void EstimateUsd_CachedRateNull_ChargesFullInputAtInputRate()
    {
        var pricing = new TokenPricing
        {
            InputUsdPer1M = 1.0,
            OutputUsdPer1M = 0,
            CachedInputUsdPer1M = null,
        };

        var cost = pricing.EstimateUsd(Delta(1000, 0, 400));

        Assert.Equal(0.001, cost, precision: 10);
    }

    [Fact]
    public void EstimateUsd_CachedGreaterThanInput_NoNegative_ChargesFullInput()
    {
        var pricing = new TokenPricing
        {
            InputUsdPer1M = 1.0,
            OutputUsdPer1M = 0,
            CachedInputUsdPer1M = 0.1,
        };

        var cost = pricing.EstimateUsd(Delta(100, 0, 400));

        Assert.True(cost >= 0);
        Assert.Equal(0.0001, cost, precision: 10);
    }

    [Fact]
    public void EstimateUsd_NoRates_ReturnsZero()
    {
        var pricing = new TokenPricing();

        Assert.False(pricing.HasAny);
        Assert.Equal(0, pricing.EstimateUsd(Delta(1000, 100, 50)));
    }

    private static TokenUsageDelta Delta(long input, long output, long cached) => new()
    {
        InputTokens = input,
        OutputTokens = output,
        TotalTokens = input + output,
        CachedInputTokens = cached,
    };
}
