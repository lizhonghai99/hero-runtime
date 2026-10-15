namespace Hero.Core.Observability;

/// <summary>
/// Right side of the Pi-style status bar: provider / model / reasoning.
/// </summary>
public sealed class StatusModelInfo
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    /// <summary>Display label for thinking/reasoning effort (e.g. none, low, high).</summary>
    public required string ReasoningLevel { get; init; }

    public int MaxContextWindowTokens { get; init; } = 128_000;

    /// <summary>Compaction mode label: none | auto (auto when compaction is wired).</summary>
    public string CompactionMode { get; init; } = "none";
}
