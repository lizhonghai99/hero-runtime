using System;

namespace Hero.Core.Session;

/// <summary>One turn node in the session tree (Pi-style branching history).</summary>
public sealed class SessionNode
{
    public required string Id { get; init; }

    public string? ParentId { get; init; }

    public required string UserPreview { get; init; }

    public string? AssistantPreview { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>Snapshot of the tree plus current tip.</summary>
public sealed class SessionTreeSnapshot
{
    public required string WorkingDirectory { get; init; }

    public required string FilePath { get; init; }

    public string? TipId { get; init; }

    public required IReadOnlyList<SessionNode> Nodes { get; init; }
}
