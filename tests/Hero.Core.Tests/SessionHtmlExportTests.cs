using Hero.Core.Observability;
using Hero.Core.Session;

namespace Hero.Core.Tests;

public sealed class SessionHtmlExportTests
{
    [Fact]
    public void RenderHtml_ContainsTreeAndAgentLabeledCalls()
    {
        var tree = new SessionTreeSnapshot
        {
            WorkingDirectory = @"D:\LearnCode\hero-runtime",
            FilePath = @"D:\x\current.jsonl",
            TipId = "n2",
            Nodes =
            [
                new SessionNode
                {
                    Id = "n1",
                    ParentId = null,
                    UserPreview = "count cs files",
                    AssistantPreview = "42 files",
                    At = DateTimeOffset.UnixEpoch,
                },
                new SessionNode
                {
                    Id = "n2",
                    ParentId = "n1",
                    UserPreview = "summarize",
                    AssistantPreview = "done",
                    At = DateTimeOffset.UnixEpoch.AddMinutes(1),
                },
            ],
        };

        var turn = new TurnTrace("turn-abc");
        turn.Calls.Add(new ModelCallRecord
        {
            TurnId = "turn-abc",
            AgentName = "hi",
            CallIndex = 1,
            StartedAt = DateTimeOffset.UtcNow,
            DurationMs = 120,
            ToolNames = ["run_shell"],
            InputTokens = 100,
            OutputTokens = 20,
            Messages = [new MessageSummary { Role = "user", Text = "count cs files", CharCount = 14 }],
        });
        turn.Calls.Add(new ModelCallRecord
        {
            TurnId = "turn-abc",
            AgentName = "ExploreAgent",
            CallIndex = 2,
            StartedAt = DateTimeOffset.UtcNow,
            DurationMs = 80,
            Streaming = true,
            ToolNames = ["read_file"],
            InputTokens = 50,
            OutputTokens = 10,
            Messages = [new MessageSummary { Role = "user", Text = "read props", CharCount = 10 }],
        });

        var html = SessionExporter.RenderHtml(tree, [turn]);

        Assert.Contains("HERO session + traces", html, StringComparison.Ordinal);
        Assert.Contains("count cs files", html, StringComparison.Ordinal);
        Assert.Contains("turn-abc", html, StringComparison.Ordinal);
        Assert.Contains("ExploreAgent", html, StringComparison.Ordinal);
        Assert.Contains("run_shell", html, StringComparison.Ordinal);
        Assert.Contains("class=\"call sub\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"call main\"", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderHtml_EmptyTraces_ShowsPlaceholder()
    {
        var tree = new SessionTreeSnapshot
        {
            WorkingDirectory = "/tmp",
            FilePath = "/tmp/current.jsonl",
            TipId = null,
            Nodes = [],
        };

        var html = SessionExporter.RenderHtml(tree, []);
        Assert.Contains("No traces recorded", html, StringComparison.Ordinal);
    }
}
