using Hero.Core.Approval;

namespace Hero.Core.Tests;

public sealed class ToolApprovalArgumentKeyTests
{
    [Fact]
    public void Compute_SameArgs_SameKey_RegardlessOfInsertionOrder()
    {
        var a = new Dictionary<string, object?>
        {
            ["command"] = "dotnet --info",
            ["timeout"] = 30,
        };
        var b = new Dictionary<string, object?>
        {
            ["timeout"] = 30,
            ["command"] = "dotnet --info",
        };

        var keyA = ToolApprovalArgumentKey.Compute("run_shell", a);
        var keyB = ToolApprovalArgumentKey.Compute("run_shell", b);
        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void Compute_DifferentArgs_DifferentKey()
    {
        var a = ToolApprovalArgumentKey.Compute(
            "run_shell",
            new Dictionary<string, object?> { ["command"] = "ls" });
        var b = ToolApprovalArgumentKey.Compute(
            "run_shell",
            new Dictionary<string, object?> { ["command"] = "pwd" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToolApprovalRequest_ExposesArgumentsKey()
    {
        var request = new ToolApprovalRequest(
            "run_shell",
            new Dictionary<string, object?> { ["command"] = "echo hi" },
            "command=echo hi");

        Assert.Equal(
            ToolApprovalArgumentKey.Compute("run_shell", request.Arguments),
            request.ArgumentsKey);
    }

    [Fact]
    public async Task Handlers_ReturnExpectedDecisions()
    {
        var request = new ToolApprovalRequest(
            "run_shell",
            new Dictionary<string, object?>(),
            string.Empty);

        Assert.Equal(
            ToolApprovalDecision.Reject,
            await new RejectingToolApprovalHandler().RequestApprovalAsync(request));
        Assert.Equal(
            ToolApprovalDecision.ApproveOnce,
            await new AutoApprovingToolApprovalHandler().RequestApprovalAsync(request));
    }
}
