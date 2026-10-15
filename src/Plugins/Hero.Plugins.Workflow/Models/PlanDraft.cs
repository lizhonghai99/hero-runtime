namespace Hero.Plugins.Workflow.Models;

/// <summary>Plan produced by <see cref="Executors.PlanExecutor"/> before HITL approval.</summary>
public sealed record PlanDraft(string Task, string Plan);
