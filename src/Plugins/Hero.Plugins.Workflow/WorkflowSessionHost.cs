using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Convention;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hero.Plugins.Workflow;

/// <summary>
/// Owns workflow config, optional chat client (for research agent), and run entry points.
/// </summary>
internal sealed class WorkflowSessionHost : IAsyncDisposable
{
    private readonly string _workingDirectory;
    private readonly ILogger _logger;
    private readonly WorkflowRunner _runner = new();
    private readonly object _gate = new();
    private IChatClient? _chatClient;
    private WorkflowConfigDocument _config;
    private bool _disposed;

    public WorkflowSessionHost(string workingDirectory, ILogger? logger = null)
    {
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(workingDirectory);
        _logger = logger ?? NullLogger.Instance;
        _config = WorkflowConfigLoader.LoadMerged(_workingDirectory);
    }

    public bool IsEnabled => WorkflowConfigLoader.IsEnabled(_config);

    public WorkflowConfigDocument Config => _config;

    /// <summary>Optional override for tests / --yes hosts.</summary>
    public WorkflowApprovalHandler? ApprovalHandler { get; set; }

    public void ReloadConfig()
    {
        _config = WorkflowConfigLoader.LoadMerged(_workingDirectory);
    }

    /// <summary>Capture the shared chat client (pass-through decorator from plugin configure).</summary>
    public IChatClient AttachChatClient(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            _chatClient = client;
        }

        return client;
    }

    public string BuildInstructionAppendix()
    {
        if (!IsEnabled)
        {
            return string.Empty;
        }

        return
            """
            Workflow plugin:
            - run_workflow(task): run the multi-step research pipeline (plan → human approve → research agent → summarize).
            Prefer run_workflow for predefined multi-step research; keep the interactive agent loop for open-ended chat.
            """;
    }

    public string DescribeStatus()
    {
        var project = WorkflowConfigLoader.ProjectConfigPath(_workingDirectory);
        var user = WorkflowConfigLoader.UserConfigPath;
        if (!IsEnabled)
        {
            return $"workflow: idle (set \"enabled\": true in {project} or {user})";
        }

        return
            $"workflow: enabled; checkpoint={_config.Checkpoint == true}; " +
            $"timeoutSeconds={_config.TimeoutSeconds ?? 0}; autoApprove={_config.AutoApprove == true}";
    }

    public AITool CreateRunWorkflowTool()
    {
        return AIFunctionFactory.Create(
            RunWorkflowToolAsync,
            name: "run_workflow",
            description:
            "Run the multi-step research workflow (plan → HITL approve → research → summarize). " +
            "Use for predefined multi-step orchestration, not for ordinary chat turns.");
    }

    private async Task<string> RunWorkflowToolAsync(
        [Description("Task description for the research pipeline")] string task,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(task, cancellationToken).ConfigureAwait(false);
        if (result.Cancelled)
        {
            return "workflow cancelled or timed out.";
        }

        if (!result.Succeeded)
        {
            return $"workflow failed: {result.Status}";
        }

        return result.Output ?? "(empty)";
    }

    public async Task<WorkflowRunResult> RunAsync(string task, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsEnabled)
        {
            return new WorkflowRunResult(false, null, "disabled");
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return new WorkflowRunResult(false, null, "empty-task");
        }

        IChatClient? client;
        lock (_gate)
        {
            client = _chatClient;
        }

        AIAgent researchAgent = client is null
            ? CreateFallbackAgent()
            : CreateResearchAgent(client);

        var workflow = ResearchPipelineFactory.Build(researchAgent);
        var approval = ResolveApprovalHandler();
        var runOptions = new WorkflowRunOptions
        {
            Timeout = ResolveTimeout(),
            EnableCheckpoint = _config.Checkpoint == true,
            CheckpointDirectory = Path.Combine(
                ConventionPaths.ProjectRoot(_workingDirectory),
                "workflow-checkpoints"),
        };

        _logger.LogDebug("Starting research workflow for task length={Length}", task.Length);
        return await _runner.RunAsync(workflow, task.Trim(), approval, runOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private WorkflowApprovalHandler ResolveApprovalHandler()
    {
        if (ApprovalHandler is not null)
        {
            return ApprovalHandler;
        }

        if (_config.AutoApprove == true)
        {
            return static (_, _) => Task.FromResult(true);
        }

        return WorkflowConsoleApproval.PromptAsync;
    }

    private TimeSpan? ResolveTimeout()
    {
        var seconds = _config.TimeoutSeconds ?? 0;
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static AIAgent CreateResearchAgent(IChatClient chatClient)
    {
        return chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "WorkflowResearchAgent",
            Description = "Researches an approved plan and returns concise notes for the workflow summarizer.",
            ChatOptions = new ChatOptions
            {
                Instructions =
                    """
                    You are WorkflowResearchAgent, a node inside a MAF workflow.
                    Given a task and an approved plan, return concise factual research notes.
                    Do not ask questions. Do not invent sources.
                    """,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
            },
        });
    }

    /// <summary>When chat is not ready yet, keep the graph runnable with a stub agent.</summary>
    private static AIAgent CreateFallbackAgent()
    {
        var echo = new EchoChatClient();
        return echo.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "WorkflowResearchAgent",
            Description = "Fallback research node (echo).",
            ChatOptions = new ChatOptions
            {
                Instructions = "Echo research notes.",
            },
        });
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Minimal chat client so the graph can run without a configured LLM (deterministic notes).</summary>
    private sealed class EchoChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("workflow-echo");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var last = messages.LastOrDefault()?.Text ?? "";
            var notes = "Research notes (fallback): " + Truncate(last, 240);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, notes)]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static string Truncate(string value, int max)
        {
            var one = value.Replace('\n', ' ').Trim();
            return one.Length <= max ? one : one[..(max - 3)] + "...";
        }
    }
}
