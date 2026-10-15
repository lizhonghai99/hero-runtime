using Hero.Plugins.Workflow.Executors;

namespace Hero.Core.Tests;

public sealed class WorkflowPlanExecutorTests
{
    [Fact]
    public void BuildPlan_IncludesTaskInSteps()
    {
        var draft = PlanExecutor.BuildPlan("compare Kimi endpoints");
        Assert.Equal("compare Kimi endpoints", draft.Task);
        Assert.Contains("compare Kimi endpoints", draft.Plan);
        Assert.Contains("1.", draft.Plan);
        Assert.Contains("2.", draft.Plan);
        Assert.Contains("3.", draft.Plan);
    }

    [Fact]
    public void BuildPlan_EmptyTask_UsesPlaceholder()
    {
        var draft = PlanExecutor.BuildPlan("  ");
        Assert.Equal("(empty task)", draft.Task);
        Assert.Contains("(empty task)", draft.Plan);
    }
}
