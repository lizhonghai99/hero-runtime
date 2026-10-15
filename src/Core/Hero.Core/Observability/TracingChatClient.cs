using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Hero.Core.Observability;

/// <summary>
/// Records each model round (messages summary + tools + usage) into <see cref="ChatTraceStore"/>.
/// </summary>
public sealed class TracingChatClient : IChatClient
{
    private const int MaxMessageChars = 240;
    private readonly IChatClient _inner;
    private readonly ChatTraceStore _store;
    private readonly string _agentName;

    public TracingChatClient(IChatClient inner, ChatTraceStore store, string? agentName = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _agentName = string.IsNullOrWhiteSpace(agentName) ? "hi" : agentName.Trim();
    }

    public string AgentName => _agentName;

    public IChatClient InnerClient => _inner;

    /// <summary>Unwrap nested <see cref="TracingChatClient"/> layers to the underlying transport client.</summary>
    public static IChatClient Unwrap(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        var current = client;
        while (current is TracingChatClient traced)
        {
            current = traced._inner;
        }

        return current;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var record = BeginRecord(messages, options, streaming: false);
        try
        {
            var response = await _inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            ApplyUsage(record, response.Usage);
            Complete(record);
            return response;
        }
        catch (Exception ex)
        {
            record.Error = ex.Message;
            Complete(record);
            throw;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var record = BeginRecord(messages, options, streaming: true);
        UsageDetails? usage = null;
        var enumerator = _inner
            .GetStreamingResponseAsync(messages, options, cancellationToken)
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
                    record.Error = ex.Message;
                    throw;
                }

                if (!moved)
                {
                    break;
                }

                var update = enumerator.Current;
                if (update.Contents is not null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is UsageContent usageContent)
                        {
                            usage = usageContent.Details;
                        }
                    }
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            if (usage is not null)
            {
                ApplyUsage(record, usage);
            }

            Complete(record);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();

    private ModelCallRecord BeginRecord(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool streaming)
    {
        var turnId = _store.ActiveTurnId ?? "orphan";
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var tools = options?.Tools?
            .Select(t => t.Name ?? t.GetType().Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        return new ModelCallRecord
        {
            TurnId = turnId,
            AgentName = _agentName,
            StartedAt = DateTimeOffset.UtcNow,
            Streaming = streaming,
            Messages = list.Select(Summarize).ToList(),
            ToolNames = tools,
        };
    }

    private void Complete(ModelCallRecord record)
    {
        record.EndedAt = DateTimeOffset.UtcNow;
        record.DurationMs = (record.EndedAt.Value - record.StartedAt).TotalMilliseconds;
        _store.AddCall(record);
    }

    private static void ApplyUsage(ModelCallRecord record, UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        record.InputTokens = usage.InputTokenCount ?? 0;
        record.OutputTokens = usage.OutputTokenCount ?? 0;
        record.CachedInputTokens = TokenUsageDelta.FromUsageDetails(usage)?.CachedInputTokens ?? 0;
    }

    private static MessageSummary Summarize(ChatMessage message)
    {
        var role = message.Role.ToString();
        var text = message.Text ?? ExtractText(message);
        var count = text.Length;
        if (text.Length > MaxMessageChars)
        {
            text = text[..MaxMessageChars] + "…";
        }

        return new MessageSummary
        {
            Role = role,
            Text = text,
            CharCount = count,
        };
    }

    private static string ExtractText(ChatMessage message)
    {
        if (message.Contents is null || message.Contents.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent tc:
                    sb.Append(tc.Text);
                    break;
                case FunctionCallContent fc:
                    sb.Append("[tool_call ").Append(fc.Name).Append(']');
                    break;
                case FunctionResultContent fr:
                    sb.Append("[tool_result ").Append(fr.CallId).Append(']');
                    break;
                default:
                    sb.Append('[').Append(content.GetType().Name).Append(']');
                    break;
            }
        }

        return sb.ToString();
    }
}
