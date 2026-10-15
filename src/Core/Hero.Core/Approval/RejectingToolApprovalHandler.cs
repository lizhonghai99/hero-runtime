using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Approval;

/// <summary>
/// Default DI handler: reject all approval requests (fail-closed for headless hosts).
/// Use <see cref="AutoApprovingToolApprovalHandler"/> or <c>--yes</c> / <c>--tool-approval auto</c> for automation.
/// </summary>
public sealed class RejectingToolApprovalHandler : IToolApprovalHandler
{
    public Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolApprovalDecision.Reject);
}
