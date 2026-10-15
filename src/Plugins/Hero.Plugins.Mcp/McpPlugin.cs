using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hero.Plugins.Mcp;

/// <summary>
/// MCP client plugin: connects configured servers and registers tools as <c>mcp_&lt;server&gt;_&lt;tool&gt;</c>.
/// Silent when no <c>mcp.json</c> is present.
/// </summary>
public sealed class McpPlugin : IPlugin
{
    public const string PluginId = "mcp";

    private readonly ILoggerFactory _loggerFactory;
    private readonly IMcpProjectTrustGate _trustGate;
    private McpSessionHost? _host;

    public McpPlugin(
        ILoggerFactory? loggerFactory = null,
        IMcpProjectTrustGate? trustGate = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _trustGate = trustGate ?? new DenyMcpProjectTrustGate();
    }

    public string Id => PluginId;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var logger = _loggerFactory.CreateLogger("Hero.Plugins.Mcp");
        _host = new McpSessionHost(builder.Options.WorkingDirectory, logger, _trustGate);

        builder
            .SetMetadata("mcp", "client")
            .AddDeferredTools(DiscoverToolsAsync)
            .AddWarmup(ct => _host.EnsureWarmAsync(ct))
            .AddDisposable(_host)
            .AddSlashCommand(new McpSlashCommand(_host))
            .AppendInstructions(() => _host.BuildInstructionAppendix());
    }

    private async Task<IEnumerable<AITool>> DiscoverToolsAsync(CancellationToken cancellationToken)
    {
        if (_host is null)
        {
            return [];
        }

        await _host.EnsureWarmAsync(cancellationToken).ConfigureAwait(false);
        return _host.Tools;
    }
}
