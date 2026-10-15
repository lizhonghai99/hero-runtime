using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI.Workflows;

namespace Hero.Plugins.Workflow.Executors;

/// <summary>
/// Deterministic planner: turns a task string into a short multi-step plan (no model call).
/// </summary>
internal sealed class PlanExecutor() : Executor<string, PlanDraft>("Plan")
{
    public override ValueTask<PlanDraft> HandleAsync(
        string message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draft = BuildPlan(message);
        return ValueTask.FromResult(draft);
    }

    /// <summary>Pure logic — unit-tested without the workflow host.</summary>
    public static PlanDraft BuildPlan(string? task)
    {
        var trimmed = (task ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = "(empty task)";
        }

        var plan =
            $"""
            1. Clarify scope for: {trimmed}
            2. Gather key facts / constraints
            3. Outline findings relevant to the task
            """;

        return new PlanDraft(trimmed, plan.Replace("\r\n", "\n"));
    }
}
