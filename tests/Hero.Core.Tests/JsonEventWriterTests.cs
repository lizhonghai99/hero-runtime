using System.Text.Json;
using Hero.Cli;
using Hero.Core.Agent;
using Hero.Core.Observability;
using Xunit;

namespace Hero.Core.Tests;

public sealed class JsonEventWriterTests
{
    [Fact]
    public void FromChunk_Text_EscapesSpecialChars()
    {
        var line = JsonEventWriter.FromChunk(
            new AgentStreamChunk(AgentStreamKind.Text, "说\"你好\"\n下一行"));
        Assert.NotNull(line);
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line!);
        Assert.Equal("text", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("说\"你好\"\n下一行", doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void FromChunk_Tool_And_Approval()
    {
        var tool = JsonEventWriter.FromChunk(new AgentStreamChunk(AgentStreamKind.Tool, "run_shell(ls)"));
        var approval = JsonEventWriter.FromChunk(new AgentStreamChunk(AgentStreamKind.Approval, "approved"));
        Assert.Contains("\"type\":\"tool\"", tool, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"run_shell(ls)\"", tool, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"approval\"", approval, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"approved\"", approval, StringComparison.Ordinal);
    }

    [Fact]
    public void FromChunk_Usage_UsesCamelCaseFields()
    {
        var line = JsonEventWriter.FromChunk(new AgentStreamChunk(new TokenUsageDelta
        {
            InputTokens = 10,
            OutputTokens = 3,
            TotalTokens = 13,
            CachedInputTokens = 2,
        }));
        Assert.Equal(
            """{"type":"usage","input":10,"output":3,"total":13,"cached":2}""",
            line);
    }

    [Fact]
    public void FromChunk_EmptyText_ReturnsNull()
    {
        Assert.Null(JsonEventWriter.FromChunk(new AgentStreamChunk(AgentStreamKind.Text, "")));
    }

    [Fact]
    public void Accumulate_And_TurnEnd_SumsUsage()
    {
        long input = 0, output = 0, total = 0, cached = 0;
        JsonEventWriter.Accumulate(ref input, ref output, ref total, ref cached, new TokenUsageDelta
        {
            InputTokens = 100,
            OutputTokens = 20,
            TotalTokens = 120,
            CachedInputTokens = 5,
        });
        JsonEventWriter.Accumulate(ref input, ref output, ref total, ref cached, new TokenUsageDelta
        {
            InputTokens = 50,
            OutputTokens = 10,
            TotalTokens = 60,
            CachedInputTokens = 1,
        });

        var line = JsonEventWriter.TurnEnd(
            status: "ok",
            durationMs: 3200,
            inputTokens: input,
            outputTokens: output,
            totalTokens: total,
            sessionId: "sess-1");

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        Assert.Equal("turn_end", root.GetProperty("type").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(3200, root.GetProperty("durationMs").GetInt64());
        Assert.Equal(150, root.GetProperty("inputTokens").GetInt64());
        Assert.Equal(30, root.GetProperty("outputTokens").GetInt64());
        Assert.Equal(180, root.GetProperty("totalTokens").GetInt64());
        Assert.Equal("sess-1", root.GetProperty("sessionId").GetString());
    }

    [Fact]
    public void Error_SerializesMessage()
    {
        var line = JsonEventWriter.Error("chat not configured");
        Assert.Equal("""{"type":"error","message":"chat not configured"}""", line);
    }
}
