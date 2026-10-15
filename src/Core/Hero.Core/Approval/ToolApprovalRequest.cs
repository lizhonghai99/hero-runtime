using System.Collections.Generic;

namespace Hero.Core.Approval;

/// <summary>
/// Structured approval request passed to <see cref="IToolApprovalHandler"/>.
/// </summary>
public sealed record ToolApprovalRequest(
    string ToolName,
    IReadOnlyDictionary<string, object?> Arguments,
    string? ArgumentsPreview = null)
{
    /// <summary>Stable key for tool+arguments standing rules (Web / host caches).</summary>
    public string ArgumentsKey => ToolApprovalArgumentKey.Compute(ToolName, Arguments);
}
