using System;
using System.Collections.Generic;

namespace Hero.Core.Observability;

public sealed class TurnRecord
{
    public required string TurnId { get; init; }

    public required string SessionId { get; set; }

    public string? WorkingDirectory { get; set; }

    public required string UserInput { get; init; }

    public string? AssistantOutput { get; set; }

    public List<string> ToolEvents { get; init; } = [];

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    public double? DurationMs { get; set; }

    /// <summary>ok | cancelled | error</summary>
    public string Status { get; set; } = "ok";

    public string? Error { get; set; }

    public long? InputTokens { get; set; }

    public long? OutputTokens { get; set; }

    public long? TotalTokens { get; set; }

    public long? CachedInputTokens { get; set; }

    public double? EstimatedCostUsd { get; set; }
}
