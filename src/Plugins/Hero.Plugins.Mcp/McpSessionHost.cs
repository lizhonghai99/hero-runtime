using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Hero.Plugins.Mcp;

/// <summary>
/// Connects configured MCP servers, discovers tools, and owns client lifetimes.
/// Project-level <c>mcp.json</c> requires trust before spawning stdio processes.
/// </summary>
public sealed class McpSessionHost : IAsyncDisposable
{
    public const string UntrustedError = "untrusted";

    private readonly string _workingDirectory;
    private readonly ILogger _logger;
    private readonly IMcpProjectTrustGate _trustGate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<AITool> _tools = [];
    private readonly List<McpServerStatus> _statuses = [];
    private bool? _hasConfiguration;
    private bool _warmed;
    private bool _disposed;

    public McpSessionHost(
        string workingDirectory,
        ILogger logger,
        IMcpProjectTrustGate? trustGate = null)
    {
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _trustGate = trustGate ?? new DenyMcpProjectTrustGate();
    }

    public string WorkingDirectory => _workingDirectory;

    public IReadOnlyList<AITool> Tools => _tools;

    public IReadOnlyList<McpServerStatus> Statuses => _statuses;

    public bool HasConfiguration
    {
        get
        {
            if (_hasConfiguration is not null)
            {
                return _hasConfiguration.Value;
            }

            // Cheap check: user config or project file present (project may later be skipped).
            var user = McpConfigLoader.TryLoadFile(McpConfigLoader.UserConfigPath);
            if (user?.McpServers.Count > 0)
            {
                return true;
            }

            var project = McpConfigLoader.TryLoadFile(McpConfigLoader.ProjectConfigPath(_workingDirectory));
            return project?.McpServers.Count > 0;
        }
    }

    public async Task EnsureWarmAsync(CancellationToken cancellationToken = default)
    {
        if (_warmed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_warmed)
            {
                return;
            }

            await ConnectAllAsync(cancellationToken).ConfigureAwait(false);
            _warmed = true;
            _hasConfiguration = _statuses.Count > 0 || _tools.Count > 0
                || McpConfigLoader.TryLoadFile(McpConfigLoader.UserConfigPath)?.McpServers.Count > 0
                || File.Exists(McpConfigLoader.ProjectConfigPath(_workingDirectory));
        }
        finally
        {
            _gate.Release();
        }
    }

    public string BuildInstructionAppendix()
    {
        // No mcp.json → silent (do not tax the system prompt).
        if (_statuses.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            "MCP servers and tools (names are mcp_<server>_<tool>):",
        };

        foreach (var s in _statuses)
        {
            var state = s.State.ToString().ToLowerInvariant();
            lines.Add($"- {s.Name}: {state}, transport={s.Transport ?? "?"}, tools={s.ToolCount}");
            if (!string.IsNullOrWhiteSpace(s.Error))
            {
                lines.Add($"  error: {s.Error}");
            }

            foreach (var tool in s.ToolNames.Take(24))
            {
                lines.Add($"  · {tool}");
            }

            if (s.ToolNames.Count > 24)
            {
                lines.Add($"  · … +{s.ToolNames.Count - 24} more");
            }
        }

        lines.Add("Use /mcp for a live status table.");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task ConnectAllAsync(CancellationToken cancellationToken)
    {
        _tools.Clear();
        _statuses.Clear();
        await DisposeClientsAsync().ConfigureAwait(false);

        var includeProject = await ResolveProjectTrustAsync(cancellationToken).ConfigureAwait(false);
        var doc = McpConfigLoader.LoadMerged(_workingDirectory, includeProjectConfig: includeProject);
        if (doc.McpServers.Count == 0 && _statuses.Count == 0)
        {
            _logger.LogDebug("MCP: no mcp.json servers configured; plugin idle");
            return;
        }

        foreach (var (name, cfg) in doc.McpServers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = new McpServerStatus
            {
                Name = name,
                State = McpConnectionState.Connecting,
                Transport = cfg.IsStdio ? "stdio" : cfg.IsHttp ? "http" : "?",
            };
            _statuses.Add(status);

            try
            {
                await ConnectOneAsync(name, cfg, status, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                status.State = McpConnectionState.Failed;
                status.Error = ex.Message;
                _logger.LogWarning(ex, "MCP server '{Server}' failed; skipping", name);
            }
        }
    }

    private async Task<bool> ResolveProjectTrustAsync(CancellationToken cancellationToken)
    {
        var projectPath = McpConfigLoader.ProjectConfigPath(_workingDirectory);
        var projectDoc = McpConfigLoader.TryLoadFile(projectPath);
        if (projectDoc?.McpServers is null || projectDoc.McpServers.Count == 0)
        {
            return false;
        }

        string contents;
        try
        {
            contents = await File.ReadAllTextAsync(projectPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "MCP: cannot read project mcp.json; skipping project config");
            MarkProjectServersUntrusted(projectDoc);
            return false;
        }

        var hash = McpProjectTrustStore.ComputeContentHash(contents);
        var allowed = await _trustGate
            .AllowProjectMcpAsync(projectPath, hash, projectDoc.McpServers.Count, cancellationToken)
            .ConfigureAwait(false);

        if (!allowed)
        {
            _logger.LogInformation(
                "MCP: project mcp.json not trusted; loading user-level servers only ({Count} project server(s) skipped)",
                projectDoc.McpServers.Count);
            MarkProjectServersUntrusted(projectDoc);
            return false;
        }

        return true;
    }

    private void MarkProjectServersUntrusted(McpConfigDocument projectDoc)
    {
        foreach (var (name, cfg) in projectDoc.McpServers)
        {
            if (_statuses.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _statuses.Add(new McpServerStatus
            {
                Name = name,
                State = McpConnectionState.Skipped,
                Transport = cfg.IsStdio ? "stdio" : cfg.IsHttp ? "http" : "?",
                Error = UntrustedError,
            });
        }
    }

    private async Task ConnectOneAsync(
        string name,
        McpServerConfig cfg,
        McpServerStatus status,
        CancellationToken cancellationToken)
    {
        if (!cfg.IsConfigured)
        {
            status.State = McpConnectionState.Skipped;
            status.Error = "missing command or url";
            return;
        }

        IClientTransport transport;
        if (cfg.IsStdio)
        {
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = name,
                Command = cfg.Command!,
                Arguments = cfg.Args ?? [],
                WorkingDirectory = string.IsNullOrWhiteSpace(cfg.WorkingDirectory)
                    ? _workingDirectory
                    : cfg.WorkingDirectory,
                EnvironmentVariables = cfg.Env is null
                    ? null
                    : cfg.Env.ToDictionary(
                        static kv => kv.Key,
                        static kv => (string?)kv.Value),
            });
            status.Transport = "stdio";
        }
        else
        {
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(cfg.Url!, UriKind.Absolute),
                TransportMode = HttpTransportMode.AutoDetect,
                Name = name,
            });
            status.Transport = "http";
        }

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _clients.Add(client);

        IList<McpClientTool> listed;
        try
        {
            listed = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            status.State = McpConnectionState.Failed;
            status.Error = ex.Message;
            _logger.LogWarning(ex, "MCP server '{Server}' ListTools failed; skipping", name);
            return;
        }

        foreach (var tool in listed)
        {
            var prefixed = McpConfigLoader.FormatToolName(name, tool.Name);
            var aiTool = tool.WithName(prefixed);
            _tools.Add(aiTool);
            status.ToolNames.Add(prefixed);
        }

        status.ToolCount = status.ToolNames.Count;
        status.State = McpConnectionState.Connected;
        _logger.LogInformation(
            "MCP server '{Server}' connected via {Transport}: {ToolCount} tools",
            name,
            status.Transport,
            status.ToolCount);
    }

    private async Task DisposeClientsAsync()
    {
        for (var i = _clients.Count - 1; i >= 0; i--)
        {
            try
            {
                await _clients[i].DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore dispose errors
            }
        }

        _clients.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisposeClientsAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
