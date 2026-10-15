using System.Runtime.CompilerServices;
using Hero.Core.Observability;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class TracingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_RecordsOneCallWithMessagesAndTools()
    {
        var store = new ChatTraceStore();
        store.BeginTurn("turn-1");

        var inner = new FakeChatClient();
        var sut = new TracingChatClient(inner, store);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello"),
        };
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => "ok", name: "ping", description: "ping")],
        };

        await sut.GetResponseAsync(messages, options);

        var turn = store.GetLatestTurn();
        Assert.NotNull(turn);
        Assert.Single(turn.Calls);
        var call = turn.Calls[0];
        Assert.Equal("hi", call.AgentName);
        Assert.Equal(1, call.CallIndex);
        Assert.False(call.Streaming);
        Assert.Contains(call.ToolNames, n => n == "ping");
        Assert.Contains(call.Messages, m => m.Role.Contains("user", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, call.InputTokens);
        Assert.Equal(1, call.OutputTokens);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RecordsStreamingCall()
    {
        var store = new ChatTraceStore();
        store.BeginTurn("turn-2");

        var inner = new FakeChatClient();
        var sut = new TracingChatClient(inner, store);

        await foreach (var _ in sut.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "stream")]))
        {
        }

        var turn = store.GetLatestTurn();
        Assert.NotNull(turn);
        Assert.Single(turn.Calls);
        Assert.True(turn.Calls[0].Streaming);
    }

    [Fact]
    public async Task NamedClient_SetsAgentName_AndUnwrapWorks()
    {
        var store = new ChatTraceStore();
        store.BeginTurn("turn-3");
        var inner = new FakeChatClient();
        var main = new TracingChatClient(inner, store, "hi");
        var explore = new TracingChatClient(TracingChatClient.Unwrap(main), store, "ExploreAgent");

        await explore.GetResponseAsync([new ChatMessage(ChatRole.User, " dig")]);

        var call = store.GetLatestTurn()!.Calls[0];
        Assert.Equal("ExploreAgent", call.AgentName);
        Assert.Same(inner, TracingChatClient.Unwrap(explore));
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "hi")])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 3,
                    OutputTokenCount = 1,
                    TotalTokenCount = 4,
                },
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "hi");
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 2, OutputTokenCount = 1 })],
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
