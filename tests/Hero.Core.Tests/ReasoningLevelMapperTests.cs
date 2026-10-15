using Hero.Core.Options;
using Microsoft.Extensions.AI;
using Xunit;

namespace Hero.Core.Tests;

public sealed class ReasoningLevelMapperTests
{
    [Theory]
    [InlineData(null, "none", true, ReasoningEffort.None)]
    [InlineData("", "none", true, ReasoningEffort.None)]
    [InlineData("low", "low", true, ReasoningEffort.Low)]
    [InlineData("MED", "medium", true, ReasoningEffort.Medium)]
    [InlineData("high", "high", true, ReasoningEffort.High)]
    [InlineData("off", "none", true, ReasoningEffort.None)]
    public void KnownLevels_Normalize(
        string? input,
        string expectedNormalized,
        bool recognized,
        ReasoningEffort expectedEffort)
    {
        Assert.Equal(recognized, ReasoningLevelMapper.TryNormalize(input, out var normalized));
        Assert.Equal(expectedNormalized, normalized);
        Assert.Equal(expectedEffort, ReasoningLevelMapper.ToEffort(input, out var ok));
        Assert.Equal(recognized, ok);
    }

    [Fact]
    public void UnknownLevel_FallsBackToNone_Unrecognized()
    {
        Assert.False(ReasoningLevelMapper.TryNormalize("turbo", out var normalized));
        Assert.Equal("none", normalized);
        Assert.Equal(ReasoningEffort.None, ReasoningLevelMapper.ToEffort("turbo", out var ok));
        Assert.False(ok);
    }
}
