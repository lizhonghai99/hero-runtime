using Hero.Core.Approval;

namespace Hero.Core.Tests;

public sealed class ToolApprovalConsoleChoiceTests
{
    [Theory]
    [InlineData(null, ToolApprovalDecision.Reject)]
    [InlineData("", ToolApprovalDecision.Reject)]
    [InlineData("   ", ToolApprovalDecision.Reject)]
    [InlineData("1", ToolApprovalDecision.ApproveOnce)]
    [InlineData("2", ToolApprovalDecision.ApproveAlwaysTool)]
    [InlineData("3", ToolApprovalDecision.ApproveAlwaysArgs)]
    [InlineData("4", ToolApprovalDecision.Reject)]
    [InlineData("y", ToolApprovalDecision.Reject)]
    [InlineData("9", ToolApprovalDecision.Reject)]
    public void Parse_MapsChoices(string? input, ToolApprovalDecision expected)
    {
        Assert.Equal(expected, ToolApprovalConsoleChoice.Parse(input));
    }
}
