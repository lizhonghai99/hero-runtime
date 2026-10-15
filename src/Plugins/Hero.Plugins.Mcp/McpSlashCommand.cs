using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;

namespace Hero.Plugins.Mcp;

internal sealed class McpSlashCommand : ISlashCommand
{
    private readonly McpSessionHost _host;

    public McpSlashCommand(McpSessionHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public string Name => "mcp";

    public string Description => "list MCP servers, connection state, and tool counts";

    public async Task<bool> TryHandleAsync(
        string line,
        ISlashCommandContext context,
        CancellationToken cancellationToken = default)
    {
        await _host.EnsureWarmAsync(cancellationToken).ConfigureAwait(false);

        var statuses = _host.Statuses;
        if (statuses.Count == 0)
        {
            context.WriteLine("  mcp> no servers configured");
            context.WriteLine($"  mcp> project: {McpConfigLoader.ProjectConfigPath(_host.WorkingDirectory)}");
            context.WriteLine($"  mcp> user:    {McpConfigLoader.UserConfigPath}");
            return true;
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var s in statuses)
        {
            rows.Add(
            [
                s.Name,
                s.State.ToString().ToLowerInvariant(),
                s.Transport ?? "-",
                s.ToolCount.ToString(),
            ]);
        }

        context.WriteTable(["server", "state", "transport", "tools"], rows);

        foreach (var s in statuses)
        {
            if (s.State == McpConnectionState.Failed && !string.IsNullOrWhiteSpace(s.Error))
            {
                context.WriteError($"mcp/{s.Name}: {s.Error}");
            }

            foreach (var tool in s.ToolNames)
            {
                context.WriteLine($"  · {s.Name}/{tool}");
            }
        }

        return true;
    }
}
