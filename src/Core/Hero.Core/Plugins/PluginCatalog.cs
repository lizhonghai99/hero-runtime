using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;
using Hero.Core.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Core.Plugins;

/// <summary>
/// Applies all registered <see cref="IPlugin"/> contributions once per process.
/// </summary>
public sealed class PluginCatalog : IAsyncDisposable
{
    private readonly List<AITool> _tools = [];
    private readonly HashSet<string> _toolNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AIContextProvider> _providers = [];
    private readonly List<SubAgentRegistration> _subAgents = [];
    private readonly List<Func<string>> _instructionFactories = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<Func<CancellationToken, Task>> _warmups = [];
    private readonly List<Func<CancellationToken, Task<IEnumerable<AITool>>>> _deferredToolFactories = [];
    private readonly List<string> _skillsRoots = [];
    private readonly List<Func<IChatClient, IChatClient>> _chatClientDecorators = [];
    private readonly List<ISlashCommand> _slashCommands = [];
    private readonly List<AIAgent> _boundSubAgents = [];
    private readonly ToolApprovalMode _approvalMode;
    private readonly ILogger<PluginCatalog> _logger;
    private readonly SemaphoreSlim _warmupGate = new(1, 1);
    private bool _configured;
    private bool _disposed;
    private bool _warmedUp;

    public PluginCatalog(
        IEnumerable<IPlugin> plugins,
        IOptions<HeroOptions> heroOptions,
        ILogger<PluginCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(heroOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        var list = plugins.ToList();
        var builder = new PluginBuilder(heroOptions.Value);
        foreach (var plugin in list)
        {
            plugin.Configure(builder);
            logger.LogDebug("Plugin configured: {PluginId}", plugin.Id);
        }

        _approvalMode = ToolApprovalModeParser.Parse(heroOptions.Value.ToolApproval);
        foreach (var tool in builder.Tools)
        {
            RegisterTool(tool);
        }

        _providers.AddRange(builder.ContextProviders);
        var subAgentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in builder.SubAgents)
        {
            if (!subAgentNames.Add(registration.Name))
            {
                _logger.LogWarning(
                    "Duplicate sub-agent name '{Name}' ignored (first registration wins).",
                    registration.Name);
                continue;
            }

            _subAgents.Add(registration);
        }

        _instructionFactories.AddRange(builder.InstructionFactories);
        foreach (var pair in builder.Metadata)
        {
            _metadata[pair.Key] = pair.Value;
        }

        _disposables.AddRange(builder.Disposables);
        _warmups.AddRange(builder.Warmups);
        _deferredToolFactories.AddRange(builder.DeferredToolFactories);
        foreach (var root in builder.SkillsRoots)
        {
            if (!_skillsRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                _skillsRoots.Add(root);
            }
        }

        _chatClientDecorators.AddRange(builder.ChatClientDecorators);
        foreach (var slash in builder.SlashCommands)
        {
            if (_slashCommands.Any(c => string.Equals(c.Name, slash.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _slashCommands.Add(slash);
        }

        _configured = true;

        logger.LogDebug(
            "Plugin catalog ready: plugins={Count}, tools={Tools}, deferred={Deferred}, subAgents={SubAgents}",
            list.Count,
            _tools.Count,
            _deferredToolFactories.Count,
            _subAgents.Count);
    }

    public IReadOnlyList<AITool> Tools => _tools;

    public IReadOnlyList<AIContextProvider> ContextProviders => _providers;

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public IReadOnlyList<string> SkillsRoots => _skillsRoots;

    public IReadOnlyList<ISlashCommand> SlashCommands => _slashCommands;

    public string? GetMetadata(string key) =>
        _metadata.TryGetValue(key, out var value) ? value : null;

    /// <summary>Apply plugin chat-client decorators in registration order.</summary>
    public IChatClient DecorateChatClient(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        var current = chatClient;
        foreach (var decorate in _chatClientDecorators)
        {
            current = decorate(current) ?? current;
        }

        return current;
    }

    public string BuildInstructionAppendix()
    {
        if (_instructionFactories.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var factory in _instructionFactories)
        {
            string? part;
            try
            {
                part = factory();
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine(part.Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Materializes sub-agents (once).
    /// <paramref name="specializeClient"/> may wrap a per-agent client (e.g. named tracing).
    /// </summary>
    public IReadOnlyList<AIAgent> BindSubAgents(
        IChatClient chatClient,
        Func<string, IChatClient, IChatClient>? specializeClient = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        if (!_configured)
        {
            throw new InvalidOperationException("Plugin catalog is not configured.");
        }

        if (_boundSubAgents.Count > 0)
        {
            return _boundSubAgents;
        }

        foreach (var registration in _subAgents)
        {
            var client = specializeClient?.Invoke(registration.Name, chatClient) ?? chatClient;
            _boundSubAgents.Add(registration.Factory(client));
        }

        return _boundSubAgents;
    }

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        if (_warmedUp)
        {
            return;
        }

        await _warmupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_warmedUp)
            {
                return;
            }

            // Deferred tools first (e.g. MCP discovery), then other warmups (shell probe, …).
            foreach (var factory in _deferredToolFactories)
            {
                try
                {
                    var tools = await factory(cancellationToken).ConfigureAwait(false);
                    if (tools is null)
                    {
                        continue;
                    }

                    foreach (var tool in tools)
                    {
                        if (tool is null)
                        {
                            continue;
                        }

                        RegisterTool(tool);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Deferred tool factory failed; continuing without those tools");
                }
            }

            foreach (var warmup in _warmups)
            {
                await warmup(cancellationToken).ConfigureAwait(false);
            }

            _warmedUp = true;
            _logger.LogDebug("Plugin catalog warmup complete: tools={Tools}", _tools.Count);
        }
        finally
        {
            _warmupGate.Release();
        }
    }

    /// <summary>
    /// First-wins tool registration (case-insensitive name). Empty names skip the set and are kept.
    /// Dedup runs before approval wrapping so discarded duplicates are not wrapped.
    /// </summary>
    private void RegisterTool(AITool tool)
    {
        var name = tool.Name;
        if (!string.IsNullOrWhiteSpace(name) && !_toolNames.Add(name))
        {
            _logger.LogWarning(
                "Duplicate tool name '{Tool}' ignored (first registration wins).",
                name);
            return;
        }

        _tools.Add(ApplyApprovalPolicy(tool, _approvalMode));
    }

    /// <summary>
    /// <c>all</c> wraps every AIFunction in <see cref="ApprovalRequiredAIFunction"/>.
    /// <c>shell</c> is applied at the Coding plugin (<c>AsAIFunction(requireApproval: true)</c>).
    /// </summary>
    private static AITool ApplyApprovalPolicy(AITool tool, ToolApprovalMode mode)
    {
        if (!ToolApprovalModeParser.RequiresAllToolsApproval(mode))
        {
            return tool;
        }

        if (tool is ApprovalRequiredAIFunction)
        {
            return tool;
        }

        if (tool is AIFunction function)
        {
            return new ApprovalRequiredAIFunction(function);
        }

        return tool;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var agent in _boundSubAgents)
        {
            if (agent is IAsyncDisposable asyncAgent)
            {
                await asyncAgent.DisposeAsync().ConfigureAwait(false);
            }
            else if (agent is IDisposable disposableAgent)
            {
                disposableAgent.Dispose();
            }
        }

        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            await _disposables[i].DisposeAsync().ConfigureAwait(false);
        }

        _warmupGate.Dispose();
    }
}
