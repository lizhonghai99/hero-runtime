using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;
using Hero.Core.Chat;
using Hero.Core.Convention;
using Hero.Core.Observability;
using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Core.Skills;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Hero.Core.Agent;

internal partial class MafAgentRuntime
{
    private async Task<AIAgent> EnsureAgentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_agent is not null)
        {
            return _agent;
        }

        var chat = _chatOptions.Value;
        var hero = _heroOptions.Value;
        var harnessOn = _harness is not null;

        _logger.LogDebug(
            "Creating Microsoft HarnessAgent '{AgentName}' protocol={Protocol}, provider={Provider}, model={Model}, harnessFeatures={HarnessOn}",
            hero.AgentName,
            chat.Protocol,
            chat.Provider,
            chat.Model,
            harnessOn);

        await _plugins.WarmupAsync(cancellationToken).ConfigureAwait(false);

        // transport → tracing → turn-bind (force correct user) → Harness wrappers.
        // TurnBind sits outside Tracing so /trace shows the corrected outbound messages.
        var chatClient = _plugins.DecorateChatClient(ChatClientFactory.Create(chat));
        chatClient = new TurnBoundChatClient(chatClient, _traces);
        // Sub-agents get a named TracingChatClient so ModelCallRecord.AgentName is set.
        var subAgents = _plugins.BindSubAgents(
            chatClient,
            (name, shared) => new TracingChatClient(TracingChatClient.Unwrap(shared), _traces, name));

        var maxContext = hero.MaxContextWindowTokens > 0 ? hero.MaxContextWindowTokens : 128_000;
        var maxOutput = hero.MaxOutputTokens > 0 ? hero.MaxOutputTokens : 8_192;
        var compactionEnabled = IsAutoCompaction(hero.CompactionMode);

        ContextWindowCompactionStrategy? compaction = null;
        if (compactionEnabled)
        {
            var eviction = ClampUnitInterval(hero.CompactionToolEvictionThreshold, 0.5);
            var truncation = ClampUnitInterval(hero.CompactionTruncationThreshold, 0.8);
            if (truncation < eviction)
            {
                truncation = eviction;
            }

            compaction = new ContextWindowCompactionStrategy(
                maxContextWindowTokens: maxContext,
                maxOutputTokens: maxOutput,
                toolEvictionThreshold: eviction,
                truncationThreshold: truncation);

            _logger.LogDebug(
                "Compaction auto: window={Window}, maxOut={MaxOut}, eviction={Eviction}, truncation={Truncation}",
                maxContext,
                maxOutput,
                compaction.ToolEvictionThreshold,
                compaction.TruncationThreshold);
        }

        var contextProviders = new List<AIContextProvider>();
        contextProviders.AddRange(_plugins.ContextProviders);

        // Long-term file memory under ~/.hero-agent/memory/<cwd-key>/ (same tier as sessions/logs).
        // Disable harness default (cwd/agent-file-memory + random WorkingFolder) and inject our store.
        var memoryRoot = ConventionPaths.UserMemoryRoot(hero.WorkingDirectory);
        Directory.CreateDirectory(memoryRoot);
        contextProviders.Add(new FileMemoryProvider(
            new FileSystemAgentFileStore(memoryRoot),
            _ => new FileMemoryState { WorkingFolder = string.Empty }));
        _logger.LogDebug("FileMemory rooted at {MemoryRoot}", memoryRoot);

        var skillsSource = _skills.CreateSkillsSource();

        if (subAgents.Count > 0)
        {
            _logger.LogDebug(
                "BackgroundAgents bound: {Names}",
                string.Join(", ", subAgents.Select(a => a.Name)));
        }

        // Microsoft.Agents.AI.Harness — batteries-included pipeline (function invocation,
        // ToolApprovalAgent, per-call history, compaction, todo/mode, loop, Skills, OTel …).
        // HITL presentation (console prompt) stays in the host; standing "always approve"
        // rules use MAF CreateAlwaysApprove* → ToolApprovalAgent session state.
        var approvalMode = ToolApprovalModeParser.Parse(hero.ToolApproval);
        var useHarnessAutoApprove = ToolApprovalModeParser.UsesAutoHandler(approvalMode)
            || _approval is AutoApprovingToolApprovalHandler;

        var harnessOptions = new HarnessAgentOptions
        {
            Name = hero.AgentName,
            // Keep Hero's own system prompt; omit the long DefaultInstructions tax.
            HarnessInstructions = string.Empty,
            MaxContextWindowTokens = maxContext,
            MaxOutputTokens = maxOutput,
            CompactionStrategy = compaction,
            DisableCompaction = !compactionEnabled,
            // Custom FileMemoryProvider is added via AIContextProviders (path under ~/.hero-agent/memory/).
            DisableFileMemory = true,
            DisableWebSearch = true,
            // Harness built-in AgentSkillsProvider: catalog in context, bodies via load_skill (on demand).
            // No roots → disable so cwd is not scanned for random SKILL.md.
            DisableAgentSkillsProvider = skillsSource is null,
            AgentSkillsSource = skillsSource,
            ToolApprovalAgentOptions = CreateToolApprovalOptions(useHarnessAutoApprove),
            DisableTodoProvider = !harnessOn,
            DisableAgentModeProvider = !harnessOn,
            AgentModeProviderOptions = harnessOn ? CreateHarnessModeOptions() : null,
            BackgroundAgents = subAgents.Count > 0 ? subAgents : null,
            AIContextProviders = contextProviders,
            LoopEvaluators = harnessOn
                ?
                [
                    new TodoCompletionLoopEvaluator(
                        new TodoCompletionLoopEvaluatorOptions { Modes = ["execute"] }),
                ]
                : null,
            LoopAgentOptions = harnessOn ? new LoopAgentOptions { MaxIterations = 8 } : null,
            ChatOptions = new ChatOptions
            {
                Instructions = BuildInstructions(hero, _plugins, _skills),
                Tools = [.. _plugins.Tools],
                MaxOutputTokens = maxOutput,
                Reasoning = new ReasoningOptions { Effort = MapReasoningEffort(hero.ReasoningLevel) },
            },
        };

        _agent = chatClient.AsHarnessAgent(harnessOptions, _loggerFactory);
        _logger.LogDebug(
            harnessOn
                ? "HarnessAgent ready (plan/todo + todo loop)"
                : "HarnessAgent ready (--no-harness: todo/mode/loop off)");

        // ReloadAgentAsync keeps a live session; only initialize when we don't have one yet.
        if (_session is null)
        {
            await InitializeSessionAsync(_agent, hero, cancellationToken).ConfigureAwait(false);
        }

        return _agent;
    }

    private static AgentModeProviderOptions CreateHarnessModeOptions() => new()
    {
        DefaultMode = "plan",
        Modes =
        [
            new AgentModeProviderOptions.AgentMode(
                "plan",
                """
                Planning mode — for non-trivial, multi-step work only.
                Greetings, language questions, and one-shot Q&A: answer directly in the user's language.
                Do not say "I'm in plan mode", do not list capabilities, do not create todos for chit-chat.
                For real multi-step tasks: add todos with todos_add, present a short plan, ask approval
                before mode_set(execute). Prefer clarifying questions over guessing.
                Do not write plans to disk unless the user asks.
                """),
            new AgentModeProviderOptions.AgentMode(
                "execute",
                """
                Autonomous execute mode.
                Work through remaining todos, use tools and sub-agents when helpful, mark items complete
                with todos_complete, and avoid asking for confirmation on every step.
                Keep replies in the user's language.
                """),
        ],
    };

    private static ToolApprovalAgentOptions CreateToolApprovalOptions(bool approveAllTools)
    {
        if (approveAllTools)
        {
            return new ToolApprovalAgentOptions
            {
                AutoApprovalRules = [ToolApprovalAgent.AllToolsAutoApprovalRule],
            };
        }

        // Trusted local skills: auto-approve load_skill / read_skill_resource (MAF built-in rule).
        // run_skill_script and other ApprovalRequired tools still prompt.
        return new ToolApprovalAgentOptions
        {
            AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule],
        };
    }

    private async Task InitializeSessionAsync(AIAgent agent, HeroOptions hero, CancellationToken cancellationToken)
    {
        var filePath = _sessionStore.GetSessionFilePath(hero.WorkingDirectory);
        _sessionRun.SessionFilePath = filePath;

        if (_sessionRun.Continue)
        {
            var loaded = await _sessionStore.TryLoadLatestAsync(hero.WorkingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                _session = await agent.DeserializeSessionAsync(loaded.Session, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _sessionId = loaded.SessionId;
                _sessionRun.ActiveSessionId = _sessionId;
                _sessionRun.Resumed = true;
                SyncTranscriptFromSession();
                _logger.LogInformation("Resumed session {SessionId} from {Path}", _sessionId, loaded.FilePath);
                return;
            }

            _logger.LogWarning("No session snapshot found for continue; starting a new session ({Path})", filePath);
        }

        _sessionId = Guid.NewGuid().ToString("N");
        _session = await agent.CreateSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await _sessionStore.BeginNewAsync(hero.WorkingDirectory, _sessionId, cancellationToken).ConfigureAwait(false);
        _sessionRun.ActiveSessionId = _sessionId;
        _sessionRun.Resumed = false;
        _transcript.Clear();
        _logger.LogInformation("Started session {SessionId} at {Path}", _sessionId, filePath);
    }

    private static string BuildInstructions(HeroOptions hero, PluginCatalog plugins, SkillsHost skills)
    {
        var system = ConventionPromptStore.ResolveSystemInstructions(hero);
        var agentsMd = ConventionPromptStore.TryLoadProjectAgentsMd(hero.WorkingDirectory);
        var agentsBlock = string.IsNullOrWhiteSpace(agentsMd)
            ? string.Empty
            : $"""

               ## Project AGENTS.md
               {agentsMd.Trim()}
               """;

        var skillsHint = skills.HasSkills
            ? """

              Skills (on demand): the catalog lists available skills by name only.
              When a listed skill matches the task, call load_skill before improvising —
              do not invent workflows that a skill already covers. Never expect full SKILL.md in the system prompt.
              """
            : string.Empty;

        var memoryRoot = ConventionPaths.UserMemoryRoot(hero.WorkingDirectory);
        var memoryHint = $"""

            File memory (on demand): durable notes for this working directory live under {memoryRoot}.
            Use file_memory_* tools to write/read/search — do not dump the whole store into the prompt.
            """;

        return $"""
            {system}
            {agentsBlock}

            Working directory: {hero.WorkingDirectory}
            {skillsHint}
            {memoryHint}

            Prefer tools over guessing. Keep answers short.
            Match the user's language. Skip capability menus unless asked.
            {plugins.BuildInstructionAppendix()}
            """;
    }

    private static ReasoningEffort MapReasoningEffort(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return ReasoningEffort.None;
        }

        return level.Trim().ToLowerInvariant() switch
        {
            "none" or "off" or "minimal" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" or "med" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            _ => ReasoningEffort.None,
        };
    }

    private static bool IsAutoCompaction(string? mode) =>
        string.IsNullOrWhiteSpace(mode)
        || mode.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static double ClampUnitInterval(double value, double fallback)
    {
        if (double.IsNaN(value) || value <= 0 || value > 1)
        {
            return fallback;
        }

        return value;
    }
}
