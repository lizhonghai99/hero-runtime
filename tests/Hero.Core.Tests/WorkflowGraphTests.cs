using Hero.Plugins.Workflow;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI.Workflows;

namespace Hero.Core.Tests;

public sealed class WorkflowGraphTests
{
    [Fact]
    public async Task BuildDeterministic_ApprovedPath_ProducesSummary()
    {
        var workflow = ResearchPipelineFactory.BuildDeterministic(
            static (prompt, _) => Task.FromResult("notes about " + prompt.Split('\n')[0]));

        var runner = new WorkflowRunner();
        var result = await runner.RunAsync(
            workflow,
            "cats",
            static (draft, _) =>
            {
                Assert.Equal("cats", draft.Task);
                Assert.Contains("cats", draft.Plan);
                return Task.FromResult(true);
            },
            new WorkflowRunOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.StartsWith("Summary:", result.Output);
        Assert.Contains("notes about", result.Output);
    }

    [Fact]
    public async Task BuildDeterministic_RejectedPath_SummarizesRejection()
    {
        var workflow = ResearchPipelineFactory.BuildDeterministic(
            static (_, _) => Task.FromResult("should-not-run"));

        var runner = new WorkflowRunner();
        var result = await runner.RunAsync(
            workflow,
            "dogs",
            static (_, _) => Task.FromResult(false),
            new WorkflowRunOptions(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Summary: plan was rejected; no research performed.", result.Output);
    }

    [Fact]
    public async Task Hitl_RequestPort_RoutesResponseToResearch()
    {
        var seen = new List<PlanDraft>();
        var researchCalls = 0;

        var workflow = ResearchPipelineFactory.BuildDeterministic(
            (prompt, _) =>
            {
                Interlocked.Increment(ref researchCalls);
                return Task.FromResult("ok:" + prompt.Contains("Approved plan", StringComparison.Ordinal));
            });

        await using StreamingRun handle =
            await InProcessExecution.RunStreamingAsync(workflow, "hitl-task");

        string? output = null;
        await foreach (var evt in handle.WatchStreamAsync())
        {
            if (evt is RequestInfoEvent req)
            {
                Assert.True(req.Request.Data.Is<PlanDraft>(out var draft));
                Assert.NotNull(draft);
                seen.Add(draft!);
                await handle.SendResponseAsync(
                    req.Request.CreateResponse(new PlanDecision(true, draft!.Task, draft.Plan)));
            }
            else if (evt is WorkflowOutputEvent o)
            {
                output = o.Data?.ToString();
            }
        }

        Assert.Single(seen);
        Assert.Equal("hitl-task", seen[0].Task);
        Assert.Equal(1, researchCalls);
        Assert.StartsWith("Summary:", output);
    }

    [Fact]
    public async Task Run_CancelledToken_ReturnsCancelled()
    {
        var workflow = ResearchPipelineFactory.BuildDeterministic(
            static async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return "never";
            });

        using var cts = new CancellationTokenSource();
        var runner = new WorkflowRunner();
        var runTask = runner.RunAsync(
            workflow,
            "slow",
            static (_, _) => Task.FromResult(true),
            new WorkflowRunOptions(),
            cts.Token);

        // Let the research node start, then cancel.
        await Task.Delay(50);
        await cts.CancelAsync();

        var result = await runTask;
        Assert.True(result.Cancelled);
        Assert.False(result.Succeeded);
        Assert.Equal("cancelled", result.Status);
    }

    [Fact]
    public async Task Run_Timeout_ReturnsCancelled()
    {
        var workflow = ResearchPipelineFactory.BuildDeterministic(
            static async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return "never";
            });

        var runner = new WorkflowRunner();
        var result = await runner.RunAsync(
            workflow,
            "slow",
            static (_, _) => Task.FromResult(true),
            new WorkflowRunOptions { Timeout = TimeSpan.FromMilliseconds(80) },
            CancellationToken.None);

        Assert.True(result.Cancelled);
        Assert.Equal("cancelled", result.Status);
    }
}
