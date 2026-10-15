using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;

namespace Hero.Core.Harness;

/// <summary>
/// Optional plan/execute + todos control surface for slash commands.
/// Providers themselves come from Microsoft <c>HarnessAgent</c>; the Harness plugin only reads/writes session state.
/// </summary>
public interface IHarnessControl
{
    /// <summary>True when plan/todo providers are active.</summary>
    bool IsAvailable { get; }

    Task<string> GetModeAsync(AgentSession session, CancellationToken cancellationToken = default);

    Task SetModeAsync(AgentSession session, string mode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(AgentSession session, CancellationToken cancellationToken = default);
}
