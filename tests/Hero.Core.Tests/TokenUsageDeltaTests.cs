using Hero.Core.Observability;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class TokenUsageDeltaTests
{
    [Fact]
    public void FromUsageDetails_Null_ReturnsNull()
    {
        Assert.Null(TokenUsageDelta.FromUsageDetails(null));
    }

    [Fact]
    public void FromUsageDetails_AllZero_ReturnsNull()
    {
        Assert.Null(TokenUsageDelta.FromUsageDetails(new UsageDetails
        {
            InputTokenCount = 0,
            OutputTokenCount = 0,
            TotalTokenCount = 0,
        }));
    }

    [Fact]
    public void FromUsageDetails_TotalMissing_EqualsInputPlusOutput()
    {
        var delta = TokenUsageDelta.FromUsageDetails(new UsageDetails
        {
            InputTokenCount = 10,
            OutputTokenCount = 3,
            TotalTokenCount = null,
        });

        Assert.NotNull(delta);
        Assert.Equal(10, delta.InputTokens);
        Assert.Equal(3, delta.OutputTokens);
        Assert.Equal(13, delta.TotalTokens);
        Assert.Equal(0, delta.CachedInputTokens);
    }

    [Fact]
    public void FromUsageDetails_RecognizesCachedKeyInAdditionalCounts()
    {
        var details = new UsageDetails
        {
            InputTokenCount = 100,
            OutputTokenCount = 1,
            TotalTokenCount = 101,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["cached_input_tokens"] = 40,
            },
        };

        var delta = TokenUsageDelta.FromUsageDetails(details);

        Assert.NotNull(delta);
        Assert.Equal(40, delta.CachedInputTokens);
    }

    [Fact]
    public void FromUsageDetails_RecognizesCacheSubstringKey()
    {
        var details = new UsageDetails
        {
            InputTokenCount = 50,
            OutputTokenCount = 0,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["prompt_cache_hit_tokens"] = 12,
            },
        };

        var delta = TokenUsageDelta.FromUsageDetails(details);

        Assert.NotNull(delta);
        Assert.Equal(12, delta.CachedInputTokens);
    }
}
