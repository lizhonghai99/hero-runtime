using Hero.Plugins.Workflow.Executors;

namespace Hero.Core.Tests;

public sealed class WorkflowSummarizeExecutorTests
{
    [Fact]
    public void Summarize_PrefixesNotes()
    {
        Assert.Equal("Summary: hello world", SummarizeExecutor.Summarize("hello world"));
    }

    [Fact]
    public void Summarize_Rejected_ShortCircuits()
    {
        Assert.Equal(
            "Summary: plan was rejected; no research performed.",
            SummarizeExecutor.Summarize("REJECTED: user declined"));
    }

    [Fact]
    public void Summarize_Empty_HasPlaceholder()
    {
        Assert.Equal("Summary: (no research notes)", SummarizeExecutor.Summarize(""));
    }

    [Fact]
    public void Summarize_TruncatesLongNotes()
    {
        var longNotes = new string('a', 500);
        var summary = SummarizeExecutor.Summarize(longNotes);
        Assert.StartsWith("Summary: ", summary);
        Assert.True(summary.Length < 420);
        Assert.EndsWith("...", summary);
    }
}
