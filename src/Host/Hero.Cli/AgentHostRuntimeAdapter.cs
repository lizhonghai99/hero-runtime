using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Harness;
using Hero.Core.Hosting;
using Hero.Core.Session;

namespace Hero.Cli;

/// <summary>Thin <see cref="IAgentRuntime"/> facade over <see cref="AgentHost"/> for REPL hosts.</summary>
internal sealed class AgentHostRuntimeAdapter(AgentHost host) : IAgentRuntime
{
    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
        host.EnsureReadyAsync(cancellationToken);

    public Task<string> RunTurnAsync(string userInput, CancellationToken cancellationToken = default) =>
        host.RunTurnAsync(userInput, cancellationToken);

    public IAsyncEnumerable<AgentStreamChunk> RunTurnStreamingAsync(
        string userInput,
        CancellationToken cancellationToken = default) =>
        host.RunTurnStreamingAsync(userInput, cancellationToken);

    public Task<string> GetModeAsync(CancellationToken cancellationToken = default) =>
        host.GetModeAsync(cancellationToken);

    public Task SetModeAsync(string mode, CancellationToken cancellationToken = default) =>
        host.SetModeAsync(mode, cancellationToken);

    public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(CancellationToken cancellationToken = default) =>
        host.GetTodosAsync(cancellationToken);

    public Task<SessionTreeSnapshot> GetSessionTreeAsync(CancellationToken cancellationToken = default) =>
        host.GetSessionTreeAsync(cancellationToken);

    public Task<string> CheckoutSessionNodeAsync(string nodeIdOrPrefix, CancellationToken cancellationToken = default) =>
        host.CheckoutSessionNodeAsync(nodeIdOrPrefix, cancellationToken);

    public Task BeginNewSessionAsync(CancellationToken cancellationToken = default) =>
        host.BeginNewSessionAsync(cancellationToken);

    public Task ReloadAgentAsync(CancellationToken cancellationToken = default) =>
        host.ReloadAgentAsync(cancellationToken);

    public Task<CompactResult> CompactTranscriptAsync(int keepTurns = 6, CancellationToken cancellationToken = default) =>
        host.CompactTranscriptAsync(keepTurns, cancellationToken);
}
