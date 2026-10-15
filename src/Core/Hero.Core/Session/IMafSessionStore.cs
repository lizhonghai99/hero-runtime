using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Session;

/// <summary>
/// Persists MAF <c>AgentSession</c> snapshots for a working directory (JSONL tree).
/// </summary>
public interface IMafSessionStore
{
    /// <summary>MAF session for the current tip (or last maf if no tip), if any.</summary>
    Task<MafSessionSnapshot?> TryLoadLatestAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Append a human turn + full MAF session snapshot as a new tip child of the current tip.</summary>
    Task AppendAsync(MafSessionAppendRequest request, CancellationToken cancellationToken = default);

    /// <summary>Start a fresh JSONL file for this cwd (call when not --continue).</summary>
    Task BeginNewAsync(string workingDirectory, string sessionId, CancellationToken cancellationToken = default);

    string GetSessionFilePath(string workingDirectory);

    /// <summary>Read the session tree (nodes + tip). Empty if no file.</summary>
    Task<SessionTreeSnapshot> GetTreeAsync(string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set tip to <paramref name="nodeIdOrPrefix"/> and return that node's MAF snapshot.
    /// </summary>
    Task<MafSessionSnapshot?> TryCheckoutAsync(
        string workingDirectory,
        string nodeIdOrPrefix,
        CancellationToken cancellationToken = default);

    /// <summary>Current tip node id after last load/append/checkout (may be null).</summary>
    string? CurrentTipId { get; }
}

public sealed class MafSessionSnapshot
{
    public required string SessionId { get; init; }

    public required string WorkingDirectory { get; init; }

    public required JsonElement Session { get; init; }

    public required string FilePath { get; init; }

    public string? NodeId { get; init; }
}

public sealed class MafSessionAppendRequest
{
    public required string WorkingDirectory { get; init; }

    public required string SessionId { get; init; }

    public required JsonElement Session { get; init; }

    public required string UserInput { get; init; }

    public string? AssistantOutput { get; init; }
}
