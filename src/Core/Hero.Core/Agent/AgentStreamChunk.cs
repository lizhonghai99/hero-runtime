using Hero.Core.Observability;

namespace Hero.Core.Agent;

public enum AgentStreamKind
{
    Text,
    Tool,
    Usage,
    /// <summary>Human-in-the-loop tool approval prompt (informational for the CLI).</summary>
    Approval,
}

/// <summary>
/// Streaming unit from the agent loop. CLI prints Text inline and Tool on its own line;
/// Usage updates the session status bar (not printed as chat).
/// </summary>
public sealed class AgentStreamChunk
{
    public AgentStreamChunk(AgentStreamKind kind, string text)
    {
        Kind = kind;
        Text = text;
    }

    public AgentStreamChunk(TokenUsageDelta usage)
    {
        Kind = AgentStreamKind.Usage;
        Text = string.Empty;
        Usage = usage;
    }

    public AgentStreamKind Kind { get; }

    public string Text { get; }

    public TokenUsageDelta? Usage { get; }
}
