namespace Hero.Plugins.Workflow.Models;

/// <summary>Outcome of one research-pipeline run.</summary>
public sealed record WorkflowRunResult(
    bool Succeeded,
    string? Output,
    string? Status,
    bool Cancelled = false);
