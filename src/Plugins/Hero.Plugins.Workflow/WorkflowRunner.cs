using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MafWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace Hero.Plugins.Workflow;

/// <summary>
/// Runs a compiled MAF workflow with streaming HITL (RequestInfoEvent → response).
/// </summary>
internal sealed class WorkflowRunner
{
    private readonly ILogger _logger;

    public WorkflowRunner(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<WorkflowRunResult> RunAsync(
        MafWorkflow workflow,
        string task,
        WorkflowApprovalHandler approvalHandler,
        WorkflowRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(approvalHandler);
        ArgumentNullException.ThrowIfNull(options);

        using var timeoutCts = options.Timeout is { } t && t > TimeSpan.Zero
            ? new CancellationTokenSource(t)
            : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var token = linked.Token;
        CheckpointManager? checkpointManager = null;
        FileSystemJsonCheckpointStore? store = null;

        try
        {
            if (options.EnableCheckpoint && !string.IsNullOrWhiteSpace(options.CheckpointDirectory))
            {
                var dir = new DirectoryInfo(options.CheckpointDirectory);
                dir.Create();
                store = new FileSystemJsonCheckpointStore(dir);
                checkpointManager = CheckpointManager.CreateJson(store);
            }

            await using StreamingRun handle = checkpointManager is null
                ? await InProcessExecution.RunStreamingAsync(workflow, task, cancellationToken: token)
                    .ConfigureAwait(false)
                : await InProcessExecution.RunStreamingAsync(workflow, task, checkpointManager, cancellationToken: token)
                    .ConfigureAwait(false);

            string? output = null;
            string? status = null;

            await foreach (var evt in handle.WatchStreamAsync(token).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case RequestInfoEvent requestEvt:
                        await HandleApprovalAsync(handle, requestEvt, approvalHandler, token).ConfigureAwait(false);
                        break;

                    case WorkflowOutputEvent outputEvt:
                        output = outputEvt.Data?.ToString();
                        status = "completed";
                        break;
                }
            }

            if (token.IsCancellationRequested)
            {
                return new WorkflowRunResult(false, output, "cancelled", Cancelled: true);
            }

            return new WorkflowRunResult(
                Succeeded: output is not null,
                Output: output,
                Status: status ?? (output is null ? "no-output" : "completed"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || timeoutCts?.IsCancellationRequested == true)
        {
            _logger.LogDebug("Workflow run cancelled or timed out");
            return new WorkflowRunResult(false, null, "cancelled", Cancelled: true);
        }
        finally
        {
            store?.Dispose();
        }
    }

    private static async Task HandleApprovalAsync(
        StreamingRun handle,
        RequestInfoEvent requestEvt,
        WorkflowApprovalHandler approvalHandler,
        CancellationToken cancellationToken)
    {
        var portable = requestEvt.Request.Data;
        if (!portable.Is<PlanDraft>(out var draft) || draft is null)
        {
            // Fail closed: reject unknown request payloads.
            await handle.SendResponseAsync(
                    requestEvt.Request.CreateResponse(new PlanDecision(false, "(unknown)", "", "invalid request")))
                .ConfigureAwait(false);
            return;
        }

        var approved = await approvalHandler(draft, cancellationToken).ConfigureAwait(false);
        var decision = new PlanDecision(approved, draft.Task, draft.Plan, approved ? null : "user declined");
        await handle.SendResponseAsync(requestEvt.Request.CreateResponse(decision))
            .ConfigureAwait(false);
    }
}

internal sealed class WorkflowRunOptions
{
    public TimeSpan? Timeout { get; init; }

    public bool EnableCheckpoint { get; init; }

    public string? CheckpointDirectory { get; init; }
}
