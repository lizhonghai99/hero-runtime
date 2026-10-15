namespace Hero.Core.Harness;

/// <summary>
/// Host-facing todo view (mapped from MAF TodoProvider items).
/// </summary>
public sealed class TodoSnapshot
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public bool IsComplete { get; init; }
}
