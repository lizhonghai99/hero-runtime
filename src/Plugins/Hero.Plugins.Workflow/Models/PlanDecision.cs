namespace Hero.Plugins.Workflow.Models;

/// <summary>HITL response for a <see cref="PlanDraft"/> (echoes task/plan so the next node keeps context).</summary>
public sealed record PlanDecision(bool Approved, string Task, string Plan, string? Note = null);
