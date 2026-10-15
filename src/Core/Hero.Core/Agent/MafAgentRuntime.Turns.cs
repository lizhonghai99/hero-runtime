using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;
using Hero.Core.Session;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Hero.Core.Agent;

internal partial class MafAgentRuntime
{
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
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var assistant = new StringBuilder();
        Exception? error = null;

        try
        {
            var agent = await EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
            _session ??= await agent.CreateSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _sessionId ??= Guid.NewGuid().ToString("N");

            _transcript.Add(new ChatMessage(ChatRole.User, userInput));

            // First leg: full Hero transcript (see _transcript). Further legs: HITL approvals.
            object runInput = CopyTranscript();
            const int maxApprovalRounds = 32;

            for (var round = 0; round < maxApprovalRounds; round++)
            {
                var approvals = new List<ToolApprovalRequestContent>();
                var stream = CreateRunStream(agent, runInput, _session, cancellationToken, clearHistory: round == 0);
                var enumerator = stream.GetAsyncEnumerator(cancellationToken);

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

                        var update = enumerator.Current;
                        foreach (var usage in ExtractUsage(update))
                        {
                            yield return new AgentStreamChunk(usage);
                        }

                        foreach (var toolLine in ExtractToolLines(update))
                        {
                            yield return new AgentStreamChunk(AgentStreamKind.Tool, toolLine);
                        }

                        foreach (var approval in ExtractApprovals(update))
                        {
                            approvals.Add(approval);
                        }

                        if (!string.IsNullOrEmpty(update.Text))
                        {
                            assistant.Append(update.Text);
                            yield return new AgentStreamChunk(AgentStreamKind.Text, update.Text);
                        }
                    }
                }
                finally
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }

                if (error is not null || approvals.Count == 0)
                {
                    break;
                }

                // Another agent leg is required after answering approvals.
                if (IsApprovalRoundLimitExceeded(round, maxApprovalRounds))
                {
                    error = new InvalidOperationException(
                        $"Tool approval exceeded {maxApprovalRounds} rounds; turn aborted without persisting.");
                    break;
                }

                var responseContents = new List<AIContent>();
                foreach (var request in approvals)
                {
                    var approvalRequest = ToApprovalRequest(request);
                    yield return new AgentStreamChunk(
                        AgentStreamKind.Approval,
                        $"{approvalRequest.ToolName}({approvalRequest.ArgumentsPreview})");

                    var decision = await _approval
                        .RequestApprovalAsync(approvalRequest, cancellationToken)
                        .ConfigureAwait(false);

                    yield return new AgentStreamChunk(
                        AgentStreamKind.Approval,
                        ToolApprovalResponseMapper.ToDisplayLabel(decision));

                    responseContents.Add(ToolApprovalResponseMapper.ToMafResponse(request, decision));
                }

                runInput = new ChatMessage(ChatRole.User, responseContents);
            }

            if (error is null && _session is not null && _agent is not null)
            {
                var assistantText = assistant.ToString();
                if (!string.IsNullOrEmpty(assistantText))
                {
                    _transcript.Add(new ChatMessage(ChatRole.Assistant, assistantText));
                }

                // Keep MAF session history aligned with what we actually sent/received.
                _session.SetInMemoryChatHistory(_transcript.ToList());
                await PersistAsync(userInput, assistantText, cancellationToken).ConfigureAwait(false);
            }
            else if (error is not null && _transcript.Count > 0)
            {
                // Failed turn: drop the user message we tentatively appended.
                var last = _transcript[^1];
                if (last.Role == ChatRole.User
                    && string.Equals(last.Text, userInput, StringComparison.Ordinal))
                {
                    _transcript.RemoveAt(_transcript.Count - 1);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    /// <summary>
    /// True when the current round still has pending approvals but no further agent leg is allowed.
    /// </summary>
    internal static bool IsApprovalRoundLimitExceeded(int round, int maxApprovalRounds) =>
        round + 1 >= maxApprovalRounds;

    private IAsyncEnumerable<AgentResponseUpdate> CreateRunStream(
        AIAgent agent,
        object runInput,
        AgentSession session,
        CancellationToken cancellationToken,
        bool clearHistory = false)
    {
        if (clearHistory)
        {
            // Drop any stale first-turn payload stuck inside the session provider.
            session.SetInMemoryChatHistory([]);
        }

        var messages = BuildRunMessages(runInput);
        _logger.LogDebug(
            "RunStreaming protocol-leg messages={Count} clearHistory={Clear} lastRole={Role} lastChars={Chars}",
            messages.Count,
            clearHistory,
            messages[^1].Role,
            messages[^1].Text?.Length ?? 0);

        return agent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken);
    }

    private List<ChatMessage> CopyTranscript() => _transcript.ToList();

    private void SyncTranscriptFromSession()
    {
        _transcript.Clear();
        if (_session is not null && _session.TryGetInMemoryChatHistory(out var history) && history.Count > 0)
        {
            _transcript.AddRange(history);
        }
    }

    /// <summary>
    /// Build the invocation message list (new user / approval only).
    /// Session history is owned by MAF ChatHistoryProvider (Responses store disabled).
    /// </summary>
    internal static List<ChatMessage> BuildRunMessages(object runInput)
    {
        List<ChatMessage> incoming = runInput switch
        {
            string text => [new ChatMessage(ChatRole.User, text)],
            ChatMessage message => [message],
            IList<ChatMessage> list => list.ToList(),
            IEnumerable<ChatMessage> many => many.ToList(),
            _ => throw new InvalidOperationException($"Unsupported run input type: {runInput.GetType().Name}"),
        };

        if (incoming.Count == 0)
        {
            throw new InvalidOperationException("Run input produced an empty message list.");
        }

        return incoming;
    }

    private static IEnumerable<ToolApprovalRequestContent> ExtractApprovals(AgentResponseUpdate update)
    {
        if (update.Contents is null || update.Contents.Count == 0)
        {
            yield break;
        }

        foreach (var content in update.Contents)
        {
            if (content is ToolApprovalRequestContent approval)
            {
                yield return approval;
            }
        }
    }

    private static ToolApprovalRequest ToApprovalRequest(ToolApprovalRequestContent request)
    {
        if (request.ToolCall is FunctionCallContent call)
        {
            IReadOnlyDictionary<string, object?> args = call.Arguments is null
                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                : new Dictionary<string, object?>(call.Arguments, StringComparer.Ordinal);

            var preview = call.Arguments is null ? string.Empty : FormatArgs(call.Arguments);
            return new ToolApprovalRequest(call.Name ?? "tool", args, preview);
        }

        return new ToolApprovalRequest(
            "tool",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            request.ToolCall?.ToString() ?? string.Empty);
    }

    private void EnsureHarness()
    {
        if (_harness is null)
        {
            throw new InvalidOperationException(
                "Harness plugin is not loaded. Register UseHarnessPlugin() for /mode and /todos.");
        }
    }

    private async Task PersistAsync(string userInput, string assistantOutput, CancellationToken cancellationToken)
    {
        try
        {
            var serialized = await _agent!.SerializeSessionAsync(_session!, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await _sessionStore.AppendAsync(
                new MafSessionAppendRequest
                {
                    WorkingDirectory = _heroOptions.Value.WorkingDirectory,
                    SessionId = _sessionId!,
                    Session = serialized,
                    UserInput = userInput,
                    AssistantOutput = assistantOutput,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist session snapshot");
        }
    }
}
