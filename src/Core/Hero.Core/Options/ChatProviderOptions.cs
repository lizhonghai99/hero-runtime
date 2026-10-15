namespace Hero.Core.Options;

/// <summary>
/// Model provider options. Bound from configuration section <c>Chat</c>.
/// Prefer User Secrets / env vars for <see cref="ApiKey"/> — never commit real keys.
/// Named to avoid clashing with <c>Microsoft.Extensions.AI.ChatOptions</c>.
/// </summary>
public sealed class ChatProviderOptions
{
    public const string SectionName = "Chat";

    /// <summary>
    /// Display/provider family label (DeepSeek, Moonshot, OpenAICompatible, …).
    /// </summary>
    public string Provider { get; set; } = "OpenAICompatible";

    /// <summary>Catalog id: deepseek | moonshot | …</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Wire protocol: <c>openai-responses</c> | <c>anthropic</c>.
    /// Empty means resolve from provider default / endpoint.
    /// </summary>
    public string Protocol { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
