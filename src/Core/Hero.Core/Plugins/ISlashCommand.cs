using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;

namespace Hero.Core.Plugins;

/// <summary>Plugin-contributed REPL slash command (e.g. <c>/trace</c>).</summary>
public interface ISlashCommand
{
    /// <summary>Command name without leading slash, e.g. <c>trace</c>.</summary>
    string Name { get; }

    /// <summary>One-line help text.</summary>
    string Description { get; }

    /// <summary>
    /// Handle a line that starts with <c>/{Name}</c>. Return true if consumed.
    /// </summary>
    Task<bool> TryHandleAsync(string line, ISlashCommandContext context, CancellationToken cancellationToken = default);
}

/// <summary>Minimal console sink for slash commands (keeps plugins host-agnostic).</summary>
public interface ISlashCommandContext
{
    void WriteLine(string text);

    void WriteError(string text);

    /// <summary>
    /// Tabular output. Host typically renders via <see cref="PlainTextTable"/>.
    /// </summary>
    void WriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows);

    /// <summary>Agent runtime for commands that need mode/todos/session.</summary>
    IAgentRuntime Runtime { get; }
}
