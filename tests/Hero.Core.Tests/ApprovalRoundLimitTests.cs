using Hero.Core.Agent;
using Xunit;

namespace Hero.Core.Tests;

public sealed class ApprovalRoundLimitTests
{
    [Theory]
    [InlineData(0, 32, false)]
    [InlineData(30, 32, false)]
    [InlineData(31, 32, true)]
    [InlineData(31, 1, true)]
    public void IsApprovalRoundLimitExceeded_MatchesPolicy(int round, int max, bool expected)
    {
        Assert.Equal(expected, MafAgentRuntime.IsApprovalRoundLimitExceeded(round, max));
    }
}
