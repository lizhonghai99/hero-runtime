using System.Collections.Generic;

namespace Hero.Plugins.Mcp;

public enum McpConnectionState
{
    NotConfigured = 0,
    Connecting = 1,
    Connected = 2,
    Failed = 3,
    Skipped = 4,
}

public sealed class McpServerStatus
{
    public required string Name { get; init; }

    public McpConnectionState State { get; set; } = McpConnectionState.NotConfigured;

    public string? Transport { get; set; }

    public int ToolCount { get; set; }

    public string? Error { get; set; }

    public List<string> ToolNames { get; } = [];
}
