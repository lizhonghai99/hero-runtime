using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Approval;

/// <summary>
/// Fallback host handler for automation. Prefer Harness
/// <c>ToolApprovalAgent.AllToolsAutoApprovalRule</c> (wired when this handler is registered
/// or mode is <c>auto</c>) so most calls never surface; this covers any residual prompts.
/// </summary>
public sealed class AutoApprovingToolApprovalHandler : IToolApprovalHandler
{
    public Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolApprovalDecision.ApproveOnce);
}
