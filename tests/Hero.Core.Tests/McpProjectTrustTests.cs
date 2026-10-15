using Hero.Plugins.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hero.Core.Tests;

public sealed class McpProjectTrustTests
{
    [Fact]
    public void TrustStore_RoundTripsFingerprint()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-trust-").FullName;
        try
        {
            var storePath = Path.Combine(root, "trusted-mcp.json");
            var store = new McpProjectTrustStore(storePath);
            var cfg = Path.Combine(root, "proj", ".hero-agent", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
            var hash = McpProjectTrustStore.ComputeContentHash("""{"mcpServers":{}}""");

            Assert.False(store.IsTrusted(cfg, hash));
            store.Trust(cfg, hash);
            Assert.True(store.IsTrusted(cfg, hash));
            Assert.False(store.IsTrusted(cfg, "deadbeef"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SessionHost_UntrustedProject_SkipsWithoutSpawning()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-untrust-").FullName;
        try
        {
            var projectHero = Path.Combine(root, ".hero-agent");
            Directory.CreateDirectory(projectHero);
            // Use a command that would fail loudly if spawned.
            File.WriteAllText(
                Path.Combine(projectHero, "mcp.json"),
                """
                {
                  "mcpServers": {
                    "evil": { "command": "hero-mcp-should-not-run", "args": ["--boom"] }
                  }
                }
                """);

            var host = new McpSessionHost(root, NullLogger.Instance, new DenyMcpProjectTrustGate());
            await host.EnsureWarmAsync();

            Assert.Empty(host.Tools);
            Assert.Contains(host.Statuses, s =>
                s.Name == "evil"
                && s.State == McpConnectionState.Skipped
                && s.Error == McpSessionHost.UntrustedError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadMerged_CanExcludeProject()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-exclude-").FullName;
        try
        {
            var userPath = Path.Combine(root, "user-mcp.json");
            File.WriteAllText(
                userPath,
                """{ "mcpServers": { "user": { "command": "node", "args": ["u.js"] } } }""");

            var projectHero = Path.Combine(root, ".hero-agent");
            Directory.CreateDirectory(projectHero);
            File.WriteAllText(
                Path.Combine(projectHero, "mcp.json"),
                """{ "mcpServers": { "proj": { "command": "npx", "args": ["p"] } } }""");

            var merged = McpConfigLoader.LoadMerged(root, userConfigPath: userPath, includeProjectConfig: false);
            Assert.True(merged.McpServers.ContainsKey("user"));
            Assert.False(merged.McpServers.ContainsKey("proj"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
