using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

namespace Hero.Plugins.Workflow.Executors;

/// <summary>Deterministic summarizer over research notes (no model call).</summary>
internal sealed class SummarizeExecutor() : Executor<string, string>("Summarize")
{
    public override ValueTask<string> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Summarize(message));
    }

    /// <summary>Pure logic — unit-tested without the workflow host.</summary>
    public static string Summarize(string? notes)
    {
        var body = (notes ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Summary: (no research notes)";
        }

        if (body.StartsWith("REJECTED:", StringComparison.OrdinalIgnoreCase))
        {
            return "Summary: plan was rejected; no research performed.";
        }

        // Keep it short for CLI / tool returns.
        var oneLine = body.Replace("\r\n", "\n").Replace('\n', ' ');
        if (oneLine.Length > 400)
        {
            oneLine = oneLine[..397] + "...";
        }

        return "Summary: " + oneLine;
    }
}
