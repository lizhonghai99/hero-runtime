using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Hero.Core.Observability;

/// <summary>
/// Last-mile guard before the transport client: ensure the active turn's user text
/// is actually present as the <em>latest</em> plain user message, and drop ConversationId
/// chaining that can cause OpenAI-compatible gateways to ignore new input.
/// </summary>
public sealed class TurnBoundChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ChatTraceStore _store;

    public TurnBoundChatClient(IChatClient inner, ChatTraceStore store)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (list, opts) = Bind(messages, options);
        return await _inner.GetResponseAsync(list, opts, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (list, opts) = Bind(messages, options);
        await foreach (var update in _inner.GetStreamingResponseAsync(list, opts, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();

    private (IList<ChatMessage> Messages, ChatOptions? Options) Bind(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        var list = messages as IList<ChatMessage> ?? messages.ToList();
        var expected = _store.ExpectedUserInput;

        if (!string.IsNullOrEmpty(expected))
        {
            list = EnsureExpectedUser(list, expected);
        }

        if (options?.ConversationId is not null)
        {
            options = options.Clone();
            options.ConversationId = null;
        }

        return (list, options);
    }

    /// <summary>
    /// Sticky-turn repair:
    /// - 0–1 plain user messages: replace that single sticky user (classic first-turn replay).
    /// - 2+ plain users but last ≠ expected: append expected (do not rewrite earlier history).
    /// </summary>
    internal static IList<ChatMessage> EnsureExpectedUser(IList<ChatMessage> list, string expected)
    {
        static bool IsContextUser(ChatMessage m) =>
            m.Role == ChatRole.User
            && m.Text is not null
            && m.Text.StartsWith("###", StringComparison.Ordinal);

        static bool IsPlainUser(ChatMessage m) =>
            m.Role == ChatRole.User && !IsContextUser(m);

        var plainIndexes = new List<int>();
        for (var i = 0; i < list.Count; i++)
        {
            if (IsPlainUser(list[i]))
            {
                plainIndexes.Add(i);
            }
        }

        if (plainIndexes.Count > 0)
        {
            var last = list[plainIndexes[^1]];
            if (string.Equals(last.Text, expected, StringComparison.Ordinal))
            {
                return list;
            }
        }

        // Classic sticky first-turn: only one (or zero) plain user in the outbound list.
        if (plainIndexes.Count <= 1)
        {
            var rebuilt = new List<ChatMessage>(list.Count + 1);
            var replaced = false;
            foreach (var message in list)
            {
                if (!replaced && IsPlainUser(message))
                {
                    rebuilt.Add(new ChatMessage(ChatRole.User, expected));
                    replaced = true;
                    continue;
                }

                rebuilt.Add(message);
            }

            if (!replaced)
            {
                InsertBeforeTrailingContext(rebuilt, expected);
            }

            return rebuilt;
        }

        // Multi-turn history present, but the new user turn never made it into the list.
        // Keep prior users/assistants intact; append expected before trailing ### context.
        var withNew = list.ToList();
        InsertBeforeTrailingContext(withNew, expected);
        return withNew;
    }

    private static void InsertBeforeTrailingContext(List<ChatMessage> messages, string expected)
    {
        static bool IsContextUser(ChatMessage m) =>
            m.Role == ChatRole.User
            && m.Text is not null
            && m.Text.StartsWith("###", StringComparison.Ordinal);

        var insertAt = messages.Count;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (IsContextUser(messages[i]))
            {
                insertAt = i;
                continue;
            }

            break;
        }

        messages.Insert(insertAt, new ChatMessage(ChatRole.User, expected));
    }
}
