using System;

namespace Hero.Core.Approval;

/// <summary>
/// Parses CLI numeric choices for tool approval (chapter-8 style four-way prompt).
/// </summary>
public static class ToolApprovalConsoleChoice
{
    /// <summary>
    /// [1] once · [2] always tool · [3] always args · [4]/empty/unknown] reject.
    /// </summary>
    public static ToolApprovalDecision Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ToolApprovalDecision.Reject;
        }

        var t = input.Trim();
        return t switch
        {
            "1" => ToolApprovalDecision.ApproveOnce,
            "2" => ToolApprovalDecision.ApproveAlwaysTool,
            "3" => ToolApprovalDecision.ApproveAlwaysArgs,
            "4" => ToolApprovalDecision.Reject,
            _ => ToolApprovalDecision.Reject,
        };
    }
}
