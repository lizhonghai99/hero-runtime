using System.Text.Json;
using System.Text.Json.Serialization;
using Hero.Core.Agent;
using Hero.Core.Observability;

namespace Hero.Cli;

/// <summary>
/// Pure helpers: agent stream chunks → JSONL event lines (camelCase, no indent).
/// </summary>
public static class JsonEventWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string? FromChunk(AgentStreamChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        return chunk.Kind switch
        {
            AgentStreamKind.Text when !string.IsNullOrEmpty(chunk.Text) =>
                Serialize(new TextEvent("text", chunk.Text)),
            AgentStreamKind.Tool =>
                Serialize(new TextEvent("tool", chunk.Text ?? "")),
            AgentStreamKind.Approval =>
                Serialize(new TextEvent("approval", chunk.Text ?? "")),
            AgentStreamKind.Usage when chunk.Usage is { } u =>
                Serialize(new UsageEvent(
                    "usage",
                    u.InputTokens,
                    u.OutputTokens,
                    u.TotalTokens,
                    u.CachedInputTokens)),
            _ => null,
        };
    }

    public static string TurnEnd(
        string status,
        long durationMs,
        long inputTokens,
        long outputTokens,
        long totalTokens,
        string? sessionId = null)
    {
        return Serialize(new TurnEndEvent(
            "turn_end",
            status,
            durationMs,
            inputTokens,
            outputTokens,
            totalTokens,
            sessionId));
    }

    public static string Error(string message) =>
        Serialize(new ErrorEvent("error", message ?? ""));

    /// <summary>Accumulate usage deltas for <see cref="TurnEnd"/> totals.</summary>
    public static void Accumulate(ref long input, ref long output, ref long total, ref long cached, TokenUsageDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        input += delta.InputTokens;
        output += delta.OutputTokens;
        total += delta.TotalTokens;
        cached += delta.CachedInputTokens;
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    private sealed record TextEvent(string Type, string Text);

    private sealed record UsageEvent(string Type, long Input, long Output, long Total, long Cached);

    private sealed record TurnEndEvent(
        string Type,
        string Status,
        long DurationMs,
        long InputTokens,
        long OutputTokens,
        long TotalTokens,
        string? SessionId);

    private sealed record ErrorEvent(string Type, string Message);
}
