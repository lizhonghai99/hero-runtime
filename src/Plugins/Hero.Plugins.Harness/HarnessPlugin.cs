using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Harness;
using Hero.Core.Plugins;
using Microsoft.Agents.AI;

namespace Hero.Plugins.Harness;

/// <summary>
/// Opt-in plan/execute UX for Microsoft <see cref="HarnessAgent"/>:
/// slash commands + <see cref="IHarnessControl"/> (reads session state owned by harness providers).
/// Does not assemble the agent pipeline — Core uses <c>AsHarnessAgent</c>.
/// </summary>
public sealed class HarnessPlugin : IPlugin, IHarnessControl, IAsyncDisposable
{
    public const string PluginId = "harness";

    // Session state is keyed by provider type; these instances only drive /mode and /todos.
    private readonly TodoProvider _todo = new();
    private readonly AgentModeProvider _mode = new();
    private bool _disposed;

    public string Id => PluginId;

    public bool IsAvailable => true;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder
            .AddDisposable(this)
            .AddSlashCommand(new ModeSlashCommand())
            .AddSlashCommand(new TodosSlashCommand())
            .AppendInstructions(
                """
                Harness tools: todos_* , mode_get , mode_set.
                Workflow:
                1) Casual chat → answer directly (same language as the user). No mode announcements, no capability lists.
                2) Non-trivial multi-step work → plan with todos, wait for approval, then mode_set(execute).
                3) In execute: work the todos; use plugin tools / sub-agents / skills when helpful.
                4) Prefer tools over guessing. Keep answers short.
                """);
    }

    public Task<string> GetModeAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _mode.GetModeAsync(session, cancellationToken);

    public Task SetModeAsync(AgentSession session, string mode, CancellationToken cancellationToken = default) =>
        _mode.SetModeAsync(session, mode, cancellationToken);

    public async Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var items = await _todo.GetAllTodosAsync(session, cancellationToken).ConfigureAwait(false);
        return items
            .Select(t => new TodoSnapshot
            {
                Id = t.Id.ToString(),
                Title = t.Title,
                Description = t.Description,
                IsComplete = t.IsComplete,
            })
            .ToList();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _todo.Dispose();
        _mode.Dispose();
        return ValueTask.CompletedTask;
    }
}
