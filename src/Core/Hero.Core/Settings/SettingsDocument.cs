namespace Hero.Core.Settings;

/// <summary>
/// User settings under ~/.hero-agent/settings.json (Pi-style defaults, no secrets).
/// </summary>
public sealed class SettingsDocument
{
    /// <summary>Catalog provider id, e.g. deepseek / moonshot.</summary>
    public string? DefaultProvider { get; set; }

    /// <summary>Model id for the default provider.</summary>
    public string? DefaultModel { get; set; }

    /// <summary>none | low | medium | high</summary>
    public string? DefaultThinkingLevel { get; set; }

    /// <summary>Optional endpoint override for the default provider (usually omit).</summary>
    public string? DefaultEndpoint { get; set; }

    /// <summary>openai-responses | anthropic — wire protocol for the default provider.</summary>
    public string? DefaultProtocol { get; set; }

    /// <summary>off | shell | all — which tools require human approval.</summary>
    public string? ToolApproval { get; set; }
}
