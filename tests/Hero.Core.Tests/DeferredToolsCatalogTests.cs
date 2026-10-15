using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Plugins.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hero.Core.Tests;

public sealed class DeferredToolsCatalogTests
{
    [Fact]
    public async Task WarmupAsync_RegistersDeferredTools_BeforeAgentWouldBuild()
    {
        var plugin = new FakeDeferredPlugin();
        var catalog = new PluginCatalog(
            [plugin],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        Assert.Empty(catalog.Tools);

        await catalog.WarmupAsync();

        Assert.Single(catalog.Tools);
        Assert.Equal("deferred_ping", catalog.Tools[0].Name);
    }

    [Fact]
    public async Task WarmupAsync_DeferredFactoryThrows_DoesNotBreakCatalog()
    {
        var catalog = new PluginCatalog(
            [new ThrowingDeferredPlugin()],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        await catalog.WarmupAsync();
        Assert.Empty(catalog.Tools);
    }

    [Fact]
    public async Task WarmupAsync_DeferredAiFunction_KeepsDescriptionAndParameterSchema()
    {
        var catalog = new PluginCatalog(
            [new SchemaDeferredPlugin()],
            Microsoft.Extensions.Options.Options.Create(new HeroOptions()),
            NullLogger<PluginCatalog>.Instance);

        await catalog.WarmupAsync();

        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(catalog.Tools));
        Assert.Equal("mcp_fs_read_file", tool.Name);
        Assert.Equal("Read a file from the workspace", tool.Description);
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("path", schema, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxBytes", schema, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDeferredPlugin : IPlugin
    {
        public string Id => "fake-deferred";

        public void Configure(IPluginBuilder builder)
        {
            builder.AddDeferredTools(ct =>
            {
                AITool tool = AIFunctionFactory.Create(
                    () => "pong",
                    name: "deferred_ping",
                    description: "deferred");
                return Task.FromResult<IEnumerable<AITool>>([tool]);
            });
        }
    }

    private sealed class ThrowingDeferredPlugin : IPlugin
    {
        public string Id => "throwing-deferred";

        public void Configure(IPluginBuilder builder)
        {
            builder.AddDeferredTools(_ => throw new InvalidOperationException("boom"));
        }
    }

    private sealed class SchemaDeferredPlugin : IPlugin
    {
        public string Id => "schema-deferred";

        public void Configure(IPluginBuilder builder)
        {
            builder.AddDeferredTools(_ =>
            {
                // Mirrors MCP → AIFunction: name prefix + description + inputSchema props.
                AITool tool = AIFunctionFactory.Create(
                    (string path, int maxBytes) => $"ok:{path}:{maxBytes}",
                    name: McpConfigLoader.FormatToolName("fs", "read_file"),
                    description: "Read a file from the workspace");
                return Task.FromResult<IEnumerable<AITool>>([tool]);
            });
        }
    }
}
