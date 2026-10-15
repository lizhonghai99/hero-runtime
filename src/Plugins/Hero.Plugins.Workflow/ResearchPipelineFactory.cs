using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Workflow.Executors;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using MafWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace Hero.Plugins.Workflow;

/// <summary>
/// Builds the fixed research graph:
/// Input → Plan → approve(RequestPort) → Research(AIAgent) → Summarize → Output.
/// </summary>
internal static class ResearchPipelineFactory
{
    public const string ApprovePortId = "approve";
    public const string WorkflowName = "research-pipeline";

    public static MafWorkflow Build(AIAgent researchAgent)
    {
        ArgumentNullException.ThrowIfNull(researchAgent);
        return BuildCore(new ResearchExecutor(researchAgent));
    }

    /// <summary>Deterministic graph for unit tests (no model).</summary>
    public static MafWorkflow BuildDeterministic(Func<string, CancellationToken, Task<string>> researchStub)
    {
        ArgumentNullException.ThrowIfNull(researchStub);
        return BuildCore(new ResearchExecutor(researchStub));
    }

    public static MafWorkflow BuildCore(ResearchExecutor research)
    {
        var plan = new PlanExecutor();
        var approve = RequestPort.Create<PlanDraft, PlanDecision>(ApprovePortId);
        var summarize = new SummarizeExecutor();

        return new WorkflowBuilder(plan)
            .AddEdge(plan, approve)
            .AddEdge(approve, research)
            .AddEdge(research, summarize)
            .WithOutputFrom(summarize)
            .WithName(WorkflowName)
            .WithDescription("Plan → HITL approve → research agent → summarize")
            .Build();
    }
}
