using Hero.Plugins.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hero.Core.Tests;

public sealed class McpSessionHostTests
{
    [Fact]
    public async Task EnsureWarmAsync_NoConfig_SilentIdle()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-idle-").FullName;
        try
        {
            var host = new McpSessionHost(root, NullLogger.Instance);
            await host.EnsureWarmAsync();
            Assert.Empty(host.Tools);
            Assert.Empty(host.Statuses);
            Assert.False(host.HasConfiguration);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureWarmAsync_BadStdioServer_SkippedWithoutThrowing()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-fail-").FullName;
        try
        {
            var dir = Path.Combine(root, ".hero-agent");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "mcp.json"),
                """
                {
                  "mcpServers": {
                    "broken": {
                      "command": "hero-mcp-server-that-does-not-exist-zz",
                      "args": ["--nope"]
                    }
                  }
                }
                """);

            var host = new McpSessionHost(root, NullLogger.Instance, new AllowMcpProjectTrustGate());
            var ex = await Record.ExceptionAsync(() => host.EnsureWarmAsync());
            Assert.Null(ex);
            Assert.Empty(host.Tools);
            Assert.Single(host.Statuses);
            Assert.Equal(McpConnectionState.Failed, host.Statuses[0].State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildInstructionAppendix_NoConfiguration_IsSilent()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-silent-instr-").FullName;
        try
        {
            var host = new McpSessionHost(root, NullLogger.Instance);
            Assert.Equal(string.Empty, host.BuildInstructionAppendix());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
