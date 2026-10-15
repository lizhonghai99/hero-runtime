namespace Hero.Core.Approval;

/// <summary>
/// Human decision for a tool-approval prompt (MAF HITL, four-way).
/// </summary>
public enum ToolApprovalDecision
{
    /// <summary>Approve this invocation only.</summary>
    ApproveOnce = 0,

    /// <summary>Approve this tool for the rest of the session (any arguments).</summary>
    ApproveAlwaysTool = 1,

    /// <summary>Approve this tool + exact arguments for the rest of the session.</summary>
    ApproveAlwaysArgs = 2,

    /// <summary>Reject this invocation.</summary>
    Reject = 3,
}
