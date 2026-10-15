using Hero.Plugins.Mcp;
using Xunit;

namespace Hero.Core.Tests;

public sealed class McpConfigLoaderTests
{
    [Fact]
    public void LoadMerged_MissingFiles_ReturnsEmpty()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-missing-").FullName;
        try
        {
            var doc = McpConfigLoader.LoadMerged(root, userConfigPath: Path.Combine(root, "no-user-mcp.json"));
            Assert.Empty(doc.McpServers);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadMerged_ProjectOverridesUser_OnSameServerName()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-merge-").FullName;
        try
        {
            var userPath = Path.Combine(root, "user-mcp.json");
            File.WriteAllText(
                userPath,
                """
                {
                  "mcpServers": {
                    "fs": { "command": "node", "args": ["user.js"] },
                    "demo": { "url": "http://localhost:3000/mcp" }
                  }
                }
                """);

            var projectHero = Path.Combine(root, ".hero-agent");
            Directory.CreateDirectory(projectHero);
            File.WriteAllText(
                Path.Combine(projectHero, "mcp.json"),
                """
                { "mcpServers": { "fs": { "command": "npx", "args": ["-y", "proj"] } } }
                """);

            var merged = McpConfigLoader.LoadMerged(root, userConfigPath: userPath);
            Assert.Equal("npx", merged.McpServers["fs"].Command);
            Assert.Equal(["-y", "proj"], merged.McpServers["fs"].Args);
            Assert.Equal("http://localhost:3000/mcp", merged.McpServers["demo"].Url);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryLoadFile_BadJson_ReturnsNull_DoesNotThrow()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-bad-").FullName;
        try
        {
            var path = Path.Combine(root, "mcp.json");
            File.WriteAllText(path, "{ not json");
            Assert.Null(McpConfigLoader.TryLoadFile(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("fs", "list_files", "mcp_fs_list_files")]
    [InlineData("My Server", "Do Thing!", "mcp_my_server_do_thing")]
    [InlineData("A", "b", "mcp_a_b")]
    public void FormatToolName_PrefixesAndSanitizes(string server, string tool, string expected)
    {
        Assert.Equal(expected, McpConfigLoader.FormatToolName(server, tool));
    }

    [Fact]
    public void LoadMerged_ProjectOnly_LoadsServers()
    {
        var root = Directory.CreateTempSubdirectory("hero-mcp-proj-").FullName;
        try
        {
            var dir = Path.Combine(root, ".hero-agent");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "mcp.json"),
                """
                {
                  "mcpServers": {
                    "fs": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "."] },
                    "demo": { "url": "http://localhost:3000/mcp" }
                  }
                }
                """);

            var doc = McpConfigLoader.LoadMerged(root, userConfigPath: Path.Combine(root, "missing-user.json"));
            Assert.Equal(2, doc.McpServers.Count);
            Assert.True(doc.McpServers["fs"].IsStdio);
            Assert.True(doc.McpServers["demo"].IsHttp);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
