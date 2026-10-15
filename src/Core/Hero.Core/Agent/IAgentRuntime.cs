using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Harness;
using Hero.Core.Session;

namespace Hero.Core.Agent;

/// <summary>
/// One user turn through the agent loop (model + optional tools / sub-agents).
/// </summary>
public interface IAgentRuntime
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);

    Task<string> RunTurnAsync(string userInput, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentStreamChunk> RunTurnStreamingAsync(
        string userInput,
        CancellationToken cancellationToken = default);

    Task<string> GetModeAsync(CancellationToken cancellationToken = default);

    Task SetModeAsync(string mode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(CancellationToken cancellationToken = default);

    Task<SessionTreeSnapshot> GetSessionTreeAsync(CancellationToken cancellationToken = default);

    /// <summary>Checkout a session-tree node and replace the live MAF session.</summary>
    Task<string> CheckoutSessionNodeAsync(string nodeIdOrPrefix, CancellationToken cancellationToken = default);

    /// <summary>Archive current.jsonl and start a fresh MAF session (same working directory).</summary>
    Task BeginNewSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild the agent after model/reasoning options change, keeping the live session when possible.
    /// </summary>
    Task ReloadAgentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually compact the live transcript (tool-result collapse + keep last N user turns).
    /// Uses MAF <c>CompactionProvider.CompactAsync</c>; does not invent a second summary agent.
    /// </summary>
    Task<CompactResult> CompactTranscriptAsync(int keepTurns = 6, CancellationToken cancellationToken = default);
}
