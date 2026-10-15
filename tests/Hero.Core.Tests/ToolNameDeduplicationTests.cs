using Hero.Core.Options;
using Hero.Core.Plugins;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hero.Core.Tests;

public sealed class ToolNameDeduplicationTests
{
    [Fact]
    public void DuplicateToolNames_AcrossPlugins_KeepsFirst()
    {
        var first = CreateNamedTool("read_file", "from-first");
        var second = CreateNamedTool("read_file", "from-second");
        var catalog = new PluginCatalog(
            [new StaticToolsPlugin("a", first), new StaticToolsPlugin("b", second)],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal("read_file", tool.Name);
        Assert.Equal("from-first", tool.Description);
    }

    [Fact]
    public async Task DuplicateToolName_DeferredAfterEager_KeepsEager()
    {
        var eager = CreateNamedTool("read_file", "eager");
        var catalog = new PluginCatalog(
            [
                new StaticToolsPlugin("eager", eager),
                new DeferredNamedToolPlugin("deferred", "read_file", "deferred"),
            ],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        Assert.Single(catalog.Tools);
        await catalog.WarmupAsync();

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal("read_file", tool.Name);
        Assert.Equal("eager", tool.Description);
    }

    [Fact]
    public void DistinctToolNames_AreAllKept()
    {
        var catalog = new PluginCatalog(
            [
                new StaticToolsPlugin("a", CreateNamedTool("alpha", "a")),
                new StaticToolsPlugin("b", CreateNamedTool("beta", "b")),
            ],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        Assert.Equal(2, catalog.Tools.Count);
        Assert.Contains(catalog.Tools, t => t.Name == "alpha");
        Assert.Contains(catalog.Tools, t => t.Name == "beta");
    }

    [Fact]
    public void DuplicateToolNames_CaseInsensitive_KeepsFirst()
    {
        var catalog = new PluginCatalog(
            [
                new StaticToolsPlugin("a", CreateNamedTool("Read_File", "upper")),
                new StaticToolsPlugin("b", CreateNamedTool("read_file", "lower")),
            ],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        var tool = Assert.Single(catalog.Tools);
        Assert.Equal("Read_File", tool.Name);
        Assert.Equal("upper", tool.Description);
    }

    [Fact]
    public void DuplicateSubAgentNames_KeepsFirst_BindOnce()
    {
        var firstBound = 0;
        var secondBound = 0;
        var catalog = new PluginCatalog(
            [
                new SubAgentPlugin("a", "SameAgent", "first", client =>
                {
                    firstBound++;
                    return client.AsAIAgent(new ChatClientAgentOptions
                    {
                        Name = "SameAgent",
                        ChatOptions = new ChatOptions { Instructions = "first" },
                    });
                }),
                new SubAgentPlugin("b", "SameAgent", "second", client =>
                {
                    secondBound++;
                    return client.AsAIAgent(new ChatClientAgentOptions
                    {
                        Name = "SameAgent",
                        ChatOptions = new ChatOptions { Instructions = "second" },
                    });
                }),
            ],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        var bound = catalog.BindSubAgents(new StubChatClient());
        Assert.Single(bound);
        Assert.Equal(1, firstBound);
        Assert.Equal(0, secondBound);
    }

    private static AITool CreateNamedTool(string name, string description) =>
        AIFunctionFactory.Create(() => name, name: name, description: description);

    private sealed class StaticToolsPlugin(string id, params AITool[] tools) : IPlugin
    {
        public string Id => id;

        public void Configure(IPluginBuilder builder)
        {
            foreach (var tool in tools)
            {
                builder.AddTool(tool);
            }
        }
    }

    private sealed class DeferredNamedToolPlugin(string id, string toolName, string description) : IPlugin
    {
        public string Id => id;

        public void Configure(IPluginBuilder builder)
        {
            builder.AddDeferredTools(_ =>
            {
                AITool tool = CreateNamedTool(toolName, description);
                return Task.FromResult<IEnumerable<AITool>>([tool]);
            });
        }
    }

    private sealed class SubAgentPlugin(
        string id,
        string name,
        string description,
        Func<IChatClient, AIAgent> factory) : IPlugin
    {
        public string Id => id;

        public void Configure(IPluginBuilder builder) =>
            builder.AddSubAgent(name, description, factory);
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
