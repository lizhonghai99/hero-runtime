using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;
using Hero.Core.Harness;
using Hero.Core.Observability;
using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Core.Session;
using Hero.Core.Skills;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Core.Agent;

/// <summary>
/// MAF <see cref="AIAgent"/> runtime.
/// Chat transport is selected by <see cref="ChatProviderOptions.Protocol"/>
/// (OpenAI Responses or Anthropic Messages; see <see cref="ChatClientFactory"/>).
/// </summary>
internal sealed partial class MafAgentRuntime : IAgentRuntime
{
    private readonly IOptions<HeroOptions> _heroOptions;
    private readonly IOptions<ChatProviderOptions> _chatOptions;
    private readonly SessionRunOptions _sessionRun;
    private readonly IMafSessionStore _sessionStore;
    private readonly PluginCatalog _plugins;
    private readonly SkillsHost _skills;
    private readonly IHarnessControl? _harness;
    private readonly IToolApprovalHandler _approval;
    private readonly ChatTraceStore _traces;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MafAgentRuntime> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AIAgent? _agent;
    private AgentSession? _session;
    private string? _sessionId;

    /// <summary>
    /// Hero-owned transcript (user/assistant/tool messages). MAF/Harness+DeepSeek has been
    /// observed to ignore new <see cref="AIAgent.RunStreamingAsync"/> user messages and keep
    /// replaying the first turn; we clear session chat history and pass this full list every
    /// user leg. After each successful turn, <c>CommitSuccessfulTurn</c> syncs from MAF so
    /// tool call/result messages survive into the next replay.
    /// </summary>
    private readonly List<ChatMessage> _transcript = [];

    public MafAgentRuntime(
        IOptions<HeroOptions> heroOptions,
        IOptions<ChatProviderOptions> chatOptions,
        SessionRunOptions sessionRun,
        IMafSessionStore sessionStore,
        PluginCatalog plugins,
        SkillsHost skills,
        IEnumerable<IHarnessControl> harnessControls,
        IToolApprovalHandler approvalHandler,
        ChatTraceStore traces,
        ILoggerFactory loggerFactory,
        ILogger<MafAgentRuntime> logger)
    {
        _heroOptions = heroOptions;
        _chatOptions = chatOptions;
        _sessionRun = sessionRun;
        _sessionStore = sessionStore;
        _plugins = plugins;
        _skills = skills;
        _harness = harnessControls.FirstOrDefault(h => h.IsAvailable);
        _approval = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _traces = traces ?? throw new ArgumentNullException(nameof(traces));
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureHarness();
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await _harness!.GetModeAsync(_session!, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        EnsureHarness();
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _harness!.SetModeAsync(_session!, mode, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodoSnapshot>> GetTodosAsync(CancellationToken cancellationToken = default)
    {
        EnsureHarness();
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await _harness!.GetTodosAsync(_session!, cancellationToken).ConfigureAwait(false);
    }

    public Task<SessionTreeSnapshot> GetSessionTreeAsync(CancellationToken cancellationToken = default) =>
        _sessionStore.GetTreeAsync(_heroOptions.Value.WorkingDirectory, cancellationToken);

    public async Task<string> CheckoutSessionNodeAsync(string nodeIdOrPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeIdOrPrefix);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var agent = await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
            var loaded = await _sessionStore
                .TryCheckoutAsync(_heroOptions.Value.WorkingDirectory, nodeIdOrPrefix, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is null)
            {
                throw new InvalidOperationException(
                    $"No session node matching '{nodeIdOrPrefix}'. Use /tree to list ids.");
            }

            _session = await agent.DeserializeSessionAsync(loaded.Session, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _sessionId = loaded.SessionId;
            _sessionRun.ActiveSessionId = _sessionId;
            _sessionRun.SessionFilePath = loaded.FilePath;
            _sessionRun.Resumed = true;
            SyncTranscriptFromSession();

            return loaded.NodeId ?? nodeIdOrPrefix;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginNewSessionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var hero = _heroOptions.Value;
            var agent = await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);

            _sessionId = Guid.NewGuid().ToString("N");
            _session = await agent.CreateSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await _sessionStore.BeginNewAsync(hero.WorkingDirectory, _sessionId, cancellationToken).ConfigureAwait(false);
            _sessionRun.ActiveSessionId = _sessionId;
            _sessionRun.SessionFilePath = _sessionStore.GetSessionFilePath(hero.WorkingDirectory);
            _sessionRun.Resumed = false;
            _transcript.Clear();
            _logger.LogInformation("Started new session {SessionId} via /new", _sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CompactResult> CompactTranscriptAsync(
        int keepTurns = 6,
        CancellationToken cancellationToken = default)
    {
        keepTurns = Math.Clamp(keepTurns, 1, 32);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
            if (_session is null)
            {
                return new CompactResult
                {
                    MessagesBefore = 0,
                    MessagesAfter = 0,
                    KeptTurns = keepTurns,
                };
            }

            SyncTranscriptFromSession();
            var before = _transcript.Count;
            if (before == 0)
            {
                return new CompactResult
                {
                    MessagesBefore = 0,
                    MessagesAfter = 0,
                    KeptTurns = keepTurns,
                };
            }

            // Always-trigger + default inverse target (Never): run until only the floor remains.
            CompactionStrategy strategy = new PipelineCompactionStrategy(
                new ToolResultCompactionStrategy(CompactionTriggers.Always, minimumPreservedGroups: 2),
                new SlidingWindowCompactionStrategy(CompactionTriggers.Always, minimumPreservedTurns: keepTurns));

            var compacted = (await CompactionProvider
                    .CompactAsync(strategy, _transcript, logger: _logger, cancellationToken)
                    .ConfigureAwait(false))
                .ToList();

            _transcript.Clear();
            _transcript.AddRange(compacted);
            _session.SetInMemoryChatHistory(_transcript.ToList());

            _logger.LogInformation(
                "Manual compact: {Before} → {After} messages (keepTurns={KeepTurns})",
                before,
                _transcript.Count,
                keepTurns);

            return new CompactResult
            {
                MessagesBefore = before,
                MessagesAfter = _transcript.Count,
                KeptTurns = keepTurns,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReloadAgentAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            System.Text.Json.JsonElement? snapshot = null;
            if (_agent is not null && _session is not null)
            {
                snapshot = await _agent.SerializeSessionAsync(_session, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _agent = null;

            var agent = await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                _session = await agent.DeserializeSessionAsync(snapshot.Value, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Reloaded HarnessAgent model={Model} reasoning={Reasoning}",
                _chatOptions.Value.Model,
                _heroOptions.Value.ReasoningLevel);
        }
        finally
        {
            _gate.Release();
        }
    }
}
