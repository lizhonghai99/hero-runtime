using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Approval;

/// <summary>
/// Host-provided gate for MAF <c>ToolApprovalRequestContent</c> (human-in-the-loop).
/// </summary>
public interface IToolApprovalHandler
{
    /// <summary>Ask the operator how to handle <paramref name="request"/>.</summary>
    Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}
