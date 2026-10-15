using Hero.Core.Approval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class ToolApprovalResponseMapperTests
{
    [Fact]
    public void ApproveOnce_CreatesApprovedResponse()
    {
        var request = new ToolApprovalRequestContent("req1", new FunctionCallContent("c1", "run_shell"));
        var content = ToolApprovalResponseMapper.ToMafResponse(request, ToolApprovalDecision.ApproveOnce);

        var response = Assert.IsType<ToolApprovalResponseContent>(content);
        Assert.True(response.Approved);
        Assert.Equal("req1", response.RequestId);
    }

    [Fact]
    public void Reject_CreatesRejectedResponse()
    {
        var request = new ToolApprovalRequestContent("req2", new FunctionCallContent("c2", "run_shell"));
        var content = ToolApprovalResponseMapper.ToMafResponse(request, ToolApprovalDecision.Reject);

        var response = Assert.IsType<ToolApprovalResponseContent>(content);
        Assert.False(response.Approved);
        Assert.Contains("rejected", response.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApproveAlwaysTool_SetsAlwaysApproveToolFlag()
    {
        var request = new ToolApprovalRequestContent("req3", new FunctionCallContent("c3", "run_shell"));
        var content = ToolApprovalResponseMapper.ToMafResponse(request, ToolApprovalDecision.ApproveAlwaysTool);

        var always = Assert.IsType<AlwaysApproveToolApprovalResponseContent>(content);
        Assert.True(always.AlwaysApproveTool);
        Assert.False(always.AlwaysApproveToolWithArguments);
        Assert.True(always.InnerResponse.Approved);
    }

    [Fact]
    public void ApproveAlwaysArgs_SetsAlwaysApproveArgsFlag()
    {
        var request = new ToolApprovalRequestContent("req4", new FunctionCallContent("c4", "run_shell"));
        var content = ToolApprovalResponseMapper.ToMafResponse(request, ToolApprovalDecision.ApproveAlwaysArgs);

        var always = Assert.IsType<AlwaysApproveToolApprovalResponseContent>(content);
        Assert.False(always.AlwaysApproveTool);
        Assert.True(always.AlwaysApproveToolWithArguments);
        Assert.True(always.InnerResponse.Approved);
    }
}
