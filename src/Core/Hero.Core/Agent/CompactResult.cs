namespace Hero.Core.Agent;

/// <summary>Result of a manual transcript compaction (<c>/compact</c>).</summary>
public sealed class CompactResult
{
    public required int MessagesBefore { get; init; }

    public required int MessagesAfter { get; init; }

    public required int KeptTurns { get; init; }

    public bool Changed => MessagesAfter < MessagesBefore;
}
