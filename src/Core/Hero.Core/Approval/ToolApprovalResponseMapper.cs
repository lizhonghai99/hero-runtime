using System;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Approval;

/// <summary>
/// Maps host UI decisions to MAF Harness approval content
/// (<see cref="ToolApprovalRequestContent.CreateResponse"/> /
/// <c>CreateAlwaysApproveToolResponse</c> /
/// <c>CreateAlwaysApproveToolWithArgumentsResponse</c>).
/// Standing rules are applied by Harness <see cref="ToolApprovalAgent"/>.
/// </summary>
public static class ToolApprovalResponseMapper
{
    public static AIContent ToMafResponse(ToolApprovalRequestContent request, ToolApprovalDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);

        return decision switch
        {
            ToolApprovalDecision.ApproveOnce =>
                request.CreateResponse(approved: true, reason: string.Empty),

            // MAF Harness extensions — persist tool-level standing rule in session.
            ToolApprovalDecision.ApproveAlwaysTool =>
                request.CreateAlwaysApproveToolResponse("User approved this tool for the session"),

            // MAF Harness extensions — persist tool+args standing rule in session.
            ToolApprovalDecision.ApproveAlwaysArgs =>
                request.CreateAlwaysApproveToolWithArgumentsResponse(
                    "User approved this tool with these arguments for the session"),

            ToolApprovalDecision.Reject =>
                request.CreateResponse(approved: false, reason: "User rejected this tool call."),

            _ => request.CreateResponse(approved: false, reason: "User rejected this tool call."),
        };
    }

    public static string ToDisplayLabel(ToolApprovalDecision decision) =>
        decision switch
        {
            ToolApprovalDecision.ApproveOnce => "approved (once)",
            ToolApprovalDecision.ApproveAlwaysTool => "approved (always tool)",
            ToolApprovalDecision.ApproveAlwaysArgs => "approved (always args)",
            ToolApprovalDecision.Reject => "rejected",
            _ => "rejected",
        };
}
