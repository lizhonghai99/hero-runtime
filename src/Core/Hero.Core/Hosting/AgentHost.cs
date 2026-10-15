using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Approval;
using Hero.Core.Chat;
using Hero.Core.Convention;
using Hero.Core.Harness;
using Hero.Core.Observability;
using Hero.Core.Options;
using Hero.Core.Providers;
using Hero.Core.Session;
using Hero.Core.Settings;
using Hero.Core.Skills;
using Microsoft.Extensions.Options;

namespace Hero.Core.Hosting;

/// <summary>
/// Facade for hosts (Cli / Web / App). Prefer resolving this from DI.
/// </summary>
public sealed class AgentHost
{
    private readonly IAgentRuntime _runtime;
    private readonly ITurnLogger _turnLogger;
    private readonly HeroOptions _options;
    private readonly ChatProviderOptions _chat;
    private readonly SessionRunOptions _sessionRun;
    private readonly SessionUsageStats _usage = new();
    private readonly TokenPricing? _pricing;
    private readonly ChatTraceStore _traces;
    private readonly SettingsStore _settingsStore;
    private readonly OpenAiCompatibleModelCatalog _modelCatalog;
    private readonly SkillsHost _skills;

    public AgentHost(
        IAgentRuntime runtime,
        ITurnLogger turnLogger,
        IOptions<HeroOptions> options,
        IOptions<ChatProviderOptions> chatOptions,
        SessionRunOptions sessionRun,
        ChatTraceStore traces,
        SettingsStore settingsStore,
        OpenAiCompatibleModelCatalog modelCatalog,
        SkillsHost skills)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _turnLogger = turnLogger ?? throw new ArgumentNullException(nameof(turnLogger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _chat = chatOptions?.Value ?? throw new ArgumentNullException(nameof(chatOptions));
        _sessionRun = sessionRun ?? throw new ArgumentNullException(nameof(sessionRun));
        _traces = traces ?? throw new ArgumentNullException(nameof(traces));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _skills = skills ?? throw new ArgumentNullException(nameof(skills));

        var pricing = new TokenPricing
        {
            InputUsdPer1M = _options.InputUsdPer1M,
            OutputUsdPer1M = _options.OutputUsdPer1M,
            CachedInputUsdPer1M = _options.CachedInputUsdPer1M,
        };
        _pricing = pricing.HasAny ? pricing : null;
    }

    public HeroOptions Options => _options;

    public ChatProviderOptions Chat => _chat;

    /// <summary>True when endpoint / model / API key are present for the active provider.</summary>
    public bool IsChatConfigured => _chat.IsConfigured;

    public SessionRunOptions SessionRun => _sessionRun;

    public ITurnLogger TurnLogger => _turnLogger;

    public SessionUsageStats Usage => _usage;

    public SettingsStore SettingsStore => _settingsStore;

    public SkillsHost Skills => _skills;

    /// <summary><c>~/.hero-agent/memory/&lt;cwd-key&gt;/</c> for Harness file memory.</summary>
    public string MemoryDirectory => ConventionPaths.UserMemoryRoot(_options.WorkingDirectory);

    public IReadOnlyList<string> ListMemoryFiles()
    {
        var root = MemoryDirectory;
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(rel => !rel.EndsWith("_description.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(rel => rel, StringComparer.OrdinalIgnoreCase)
                .Take(80)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public StatusModelInfo GetStatusModelInfo()
    {
        var provider = !string.IsNullOrWhiteSpace(_chat.ProviderId)
            ? (LlmProviderCatalog.Find(_chat.ProviderId)?.DisplayName ?? _chat.ProviderId)
            : (string.IsNullOrWhiteSpace(_chat.Provider) ? "unknown" : _chat.Provider);

        return new StatusModelInfo
        {
            Provider = provider,
            Model = string.IsNullOrWhiteSpace(_chat.Model) ? "?" : _chat.Model,
            ReasoningLevel = string.IsNullOrWhiteSpace(_options.ReasoningLevel) ? "none" : _options.ReasoningLevel,
            MaxContextWindowTokens = _options.MaxContextWindowTokens > 0 ? _options.MaxContextWindowTokens : 128_000,
            CompactionMode = string.IsNullOrWhiteSpace(_options.CompactionMode)
                ? "auto"
                : _options.CompactionMode.Trim().ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Resolve API key from env vars, or the in-memory key for the active provider this process.
    /// Never reads keys from disk.
    /// </summary>
    public Task<string?> ResolveApiKeyAsync(string providerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var def = LlmProviderCatalog.Find(providerId)
            ?? throw new ArgumentException($"Unknown provider '{providerId}'.", nameof(providerId));

        var fromEnv = LlmProviderCatalog.ReadEnv(def.ApiKeyEnvVars);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Task.FromResult<string?>(fromEnv);
        }

        if (string.Equals(_chat.ProviderId, def.Id, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_chat.ApiKey))
        {
            return Task.FromResult<string?>(_chat.ApiKey);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<RemoteModelInfo>> ListRemoteModelsAsync(
        string providerId,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var def = LlmProviderCatalog.Find(providerId)
            ?? throw new ArgumentException($"Unknown provider '{providerId}'.", nameof(providerId));
        return _modelCatalog.ListModelsAsync(def, apiKey, cancellationToken);
    }

    public async Task ApplyProviderSelectionAsync(
        string providerId,
        string apiKey,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var def = LlmProviderCatalog.Find(providerId)
            ?? throw new ArgumentException($"Unknown provider '{providerId}'.", nameof(providerId));

        _chat.ProviderId = def.Id;
        _chat.Provider = def.DisplayName;
        _chat.Protocol = ChatProtocolParser.ToConfigString(def.DefaultProtocol);
        _chat.Endpoint = def.DefaultEndpoint;
        _chat.ApiKey = apiKey.Trim(); // process memory only
        _chat.Model = model.Trim();

        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.ReloadAgentAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default) =>
        _runtime.EnsureReadyAsync(cancellationToken);

    public Task<string> GetModeAsync(CancellationToken cancellationToken = default) =>
        _runtime.GetModeAsync(cancellationToken);

    public Task SetModeAsync(string mode, CancellationToken cancellationToken = default) =>
        _runtime.SetModeAsync(mode, cancellationToken);

    public Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(CancellationToken cancellationToken = default) =>
        _runtime.GetTodosAsync(cancellationToken);

    public Task<SessionTreeSnapshot> GetSessionTreeAsync(CancellationToken cancellationToken = default) =>
        _runtime.GetSessionTreeAsync(cancellationToken);

    public Task<string> CheckoutSessionNodeAsync(string nodeIdOrPrefix, CancellationToken cancellationToken = default) =>
        _runtime.CheckoutSessionNodeAsync(nodeIdOrPrefix, cancellationToken);

    public Task BeginNewSessionAsync(CancellationToken cancellationToken = default) =>
        _runtime.BeginNewSessionAsync(cancellationToken);

    public Task ReloadAgentAsync(CancellationToken cancellationToken = default) =>
        _runtime.ReloadAgentAsync(cancellationToken);

    /// <summary>Manual transcript compact (MAF strategies; live context only until next turn persists).</summary>
    public Task<CompactResult> CompactTranscriptAsync(int keepTurns = 6, CancellationToken cancellationToken = default) =>
        _runtime.CompactTranscriptAsync(keepTurns, cancellationToken);

    /// <summary>Cycle reasoning: none → low → medium → high → none.</summary>
    public async Task<string> CycleReasoningAsync(CancellationToken cancellationToken = default)
    {
        var next = NormalizeReasoning(_options.ReasoningLevel) switch
        {
            "none" => "low",
            "low" => "medium",
            "medium" => "high",
            _ => "none",
        };
        _options.ReasoningLevel = next;
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.ReloadAgentAsync(cancellationToken).ConfigureAwait(false);
        return next;
    }

    public async Task SetReasoningAsync(string level, CancellationToken cancellationToken = default)
    {
        _options.ReasoningLevel = NormalizeReasoning(level);
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.ReloadAgentAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _chat.Model = model.Trim();
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
        await _runtime.ReloadAgentAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cycle to the next/previous model from the live provider list (Pi Ctrl+P).
    /// Falls back to the provider default hint when listing fails.
    /// </summary>
    public async Task<string> CycleModelAsync(int direction = 1, CancellationToken cancellationToken = default)
    {
        var def = LlmProviderCatalog.Find(_chat.ProviderId)
                  ?? LlmProviderCatalog.All[0];
        var current = _chat.Model?.Trim() ?? string.Empty;

        IReadOnlyList<RemoteModelInfo> models;
        try
        {
            var key = await ResolveApiKeyAsync(def.Id, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("No API key for " + def.DisplayName);
            }

            models = await ListRemoteModelsAsync(def.Id, key, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            var hint = def.DefaultModelHint;
            if (!string.IsNullOrWhiteSpace(hint)
                && !hint.Equals(current, StringComparison.OrdinalIgnoreCase))
            {
                await SetModelAsync(hint, cancellationToken).ConfigureAwait(false);
                return hint;
            }

            return current;
        }

        if (models.Count == 0)
        {
            return current;
        }

        var idx = 0;
        for (var i = 0; i < models.Count; i++)
        {
            if (models[i].Id.Equals(current, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        var step = direction >= 0 ? 1 : -1;
        var next = (idx + step + models.Count) % models.Count;
        var modelId = models[next].Id;
        await SetModelAsync(modelId, cancellationToken).ConfigureAwait(false);
        return modelId;
    }

    /// <summary>
    /// Persist tool-approval preference. Tools are bound at process start — restart <c>hi</c> to rebind.
    /// </summary>
    public async Task SetToolApprovalAsync(string mode, CancellationToken cancellationToken = default)
    {
        var parsed = ToolApprovalModeParser.Parse(mode);
        _options.ToolApproval = ToolApprovalModeParser.ToSettingsValue(parsed);
        await PersistSettingsAsync(cancellationToken).ConfigureAwait(false);
    }

    public string GetToolApprovalMode() =>
        ToolApprovalModeParser.ToSettingsValue(ToolApprovalModeParser.Parse(_options.ToolApproval));

    public async Task<SessionExporter.ExportResult> ExportSessionAsync(
        string? pathOrDirectory = null,
        bool html = false,
        CancellationToken cancellationToken = default)
    {
        var tree = await GetSessionTreeAsync(cancellationToken).ConfigureAwait(false);
        if (html)
        {
            await _traces.FlushPersistAsync(cancellationToken).ConfigureAwait(false);
            var traces = _traces.LoadPersistedTurns(_options.WorkingDirectory);
            return await SessionExporter
                .ExportHtmlAsync(tree, traces, pathOrDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        return await SessionExporter.ExportAsync(tree, pathOrDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SettingsDocument> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var doc = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        // Reflect live session values as the effective defaults the next save would write.
        doc.DefaultProvider ??= _chat.ProviderId;
        doc.DefaultModel ??= _chat.Model;
        doc.DefaultThinkingLevel ??= NormalizeReasoning(_options.ReasoningLevel);
        doc.ToolApproval ??= GetToolApprovalMode();
        return doc;
    }

    private async Task PersistSettingsAsync(CancellationToken cancellationToken)
    {
        var doc = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_chat.ProviderId))
        {
            doc.DefaultProvider = _chat.ProviderId;
        }

        if (!string.IsNullOrWhiteSpace(_chat.Model))
        {
            doc.DefaultModel = _chat.Model;
        }

        if (!string.IsNullOrWhiteSpace(_chat.Endpoint))
        {
            doc.DefaultEndpoint = _chat.Endpoint;
        }

        if (!string.IsNullOrWhiteSpace(_chat.Protocol))
        {
            doc.DefaultProtocol = _chat.Protocol;
        }

        doc.DefaultThinkingLevel = NormalizeReasoning(_options.ReasoningLevel);
        doc.ToolApproval = GetToolApprovalMode();
        await _settingsStore.SaveAsync(doc, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeReasoning(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return "none";
        }

        return level.Trim().ToLowerInvariant() switch
        {
            "off" or "minimal" or "none" => "none",
            "low" => "low",
            "med" or "medium" => "medium",
            "high" => "high",
            _ => "none",
        };
    }

    public async Task<string> RunTurnAsync(string userInput, CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();
        await foreach (var chunk in RunTurnStreamingAsync(userInput, cancellationToken).ConfigureAwait(false))
        {
            if (chunk.Kind == AgentStreamKind.Text)
            {
                buffer.Append(chunk.Text);
            }
        }

        return buffer.ToString();
    }

    public async IAsyncEnumerable<AgentStreamChunk> RunTurnStreamingAsync(
        string userInput,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = new TurnRecord
        {
            TurnId = Guid.NewGuid().ToString("N"),
            SessionId = _sessionRun.ActiveSessionId ?? "pending",
            WorkingDirectory = _options.WorkingDirectory,
            UserInput = userInput,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _traces.BeginTurn(turn.TurnId, expectedUserInput: userInput);

        var buffer = new StringBuilder();
        Exception? error = null;
        long turnIn = 0, turnOut = 0, turnTotal = 0, turnCached = 0;
        double turnCost = 0;

        var enumerator = _runtime
            .RunTurnStreamingAsync(userInput, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = ex;
                    break;
                }

                if (!moved)
                {
                    break;
                }

                var chunk = enumerator.Current;
                if (chunk.Kind == AgentStreamKind.Usage && chunk.Usage is not null)
                {
                    turnIn += chunk.Usage.InputTokens;
                    turnOut += chunk.Usage.OutputTokens;
                    turnTotal += chunk.Usage.TotalTokens > 0
                        ? chunk.Usage.TotalTokens
                        : chunk.Usage.InputTokens + chunk.Usage.OutputTokens;
                    turnCached += chunk.Usage.CachedInputTokens;
                    if (_pricing is not null)
                    {
                        turnCost += _pricing.EstimateUsd(chunk.Usage);
                    }

                    _usage.Add(chunk.Usage, _pricing);
                    yield return chunk;
                    continue;
                }

                if (chunk.Kind == AgentStreamKind.Tool)
                {
                    turn.ToolEvents.Add(chunk.Text);
                }
                else if (chunk.Kind == AgentStreamKind.Text)
                {
                    buffer.Append(chunk.Text);
                }

                yield return chunk;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);

            turn.SessionId = _sessionRun.ActiveSessionId ?? turn.SessionId;
            turn.AssistantOutput = buffer.Length > 0 ? buffer.ToString() : null;
            turn.EndedAt = DateTimeOffset.UtcNow;
            turn.DurationMs = (turn.EndedAt.Value - turn.StartedAt).TotalMilliseconds;
            turn.Error = error switch
            {
                null => null,
                OperationCanceledException => "cancelled",
                _ => error.Message,
            };
            turn.Status = error switch
            {
                null => "ok",
                OperationCanceledException => "cancelled",
                _ => "error",
            };

            if (turnIn > 0 || turnOut > 0 || turnTotal > 0 || turnCached > 0)
            {
                turn.InputTokens = turnIn;
                turn.OutputTokens = turnOut;
                turn.TotalTokens = turnTotal;
                turn.CachedInputTokens = turnCached;
                if (_pricing is not null)
                {
                    turn.EstimatedCostUsd = turnCost;
                }
            }

            await _turnLogger.LogAsync(turn, CancellationToken.None).ConfigureAwait(false);
            _traces.EndTurn(turn.TurnId);
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
