using System;

namespace Hero.Core.Approval;

/// <summary>
/// Which tools require human approval before invocation, and how headless hosts behave.
/// </summary>
public enum ToolApprovalMode
{
    /// <summary>
    /// No tool approval prompts — unsafe demo mode. Prefer <see cref="Shell"/> for interactive CLI.
    /// </summary>
    Off = 0,

    /// <summary>Approve <c>run_shell</c> (and other shell tools) only.</summary>
    Shell = 1,

    /// <summary>Approve every <see cref="Microsoft.Extensions.AI.AIFunction"/> tool.</summary>
    All = 2,

    /// <summary>
    /// Same tool wrapping as <see cref="All"/>, plus Harness
    /// <c>ToolApprovalAgent.AllToolsAutoApprovalRule</c> (explicit automation opt-in).
    /// </summary>
    Auto = 3,
}

public static class ToolApprovalModeParser
{
    public static ToolApprovalMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ToolApprovalMode.Off;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "off" or "none" or "false" or "0" => ToolApprovalMode.Off,
            "shell" or "sh" => ToolApprovalMode.Shell,
            "all" or "on" or "true" or "1" => ToolApprovalMode.All,
            "auto" or "yes" => ToolApprovalMode.Auto,
            _ => throw new ArgumentException(
                $"Unknown tool approval mode '{value}'. Use: off | shell | all | auto"),
        };
    }

    public static string ToSettingsValue(ToolApprovalMode mode) =>
        mode switch
        {
            ToolApprovalMode.Shell => "shell",
            ToolApprovalMode.All => "all",
            ToolApprovalMode.Auto => "auto",
            _ => "off",
        };

    public static bool RequiresShellApproval(ToolApprovalMode mode) =>
        mode is ToolApprovalMode.Shell or ToolApprovalMode.All or ToolApprovalMode.Auto;

    public static bool RequiresAllToolsApproval(ToolApprovalMode mode) =>
        mode is ToolApprovalMode.All or ToolApprovalMode.Auto;

    public static bool UsesAutoHandler(ToolApprovalMode mode) =>
        mode is ToolApprovalMode.Auto;
}
