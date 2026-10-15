using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hero.Plugins.Mcp;

/// <summary>Shape of <c>.hero-agent/mcp.json</c>.</summary>
public sealed class McpConfigDocument
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerConfig> McpServers { get; set; } =
        new(System.StringComparer.OrdinalIgnoreCase);
}

/// <summary>One MCP server entry (stdio via command, or HTTP/SSE via url).</summary>
public sealed class McpServerConfig
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("cwd")]
    public string? WorkingDirectory { get; set; }

    public bool IsStdio => !string.IsNullOrWhiteSpace(Command);

    public bool IsHttp => !string.IsNullOrWhiteSpace(Url);

    public bool IsConfigured => IsStdio || IsHttp;
}
