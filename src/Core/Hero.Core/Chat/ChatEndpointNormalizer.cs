using System;
using Hero.Core.Providers;

namespace Hero.Core.Chat;

/// <summary>Normalize provider base URLs for the selected protocol.</summary>
internal static class ChatEndpointNormalizer
{
    public static string Normalize(string? endpoint, ChatProtocol protocol, LlmProviderDefinition? def)
    {
        var value = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(value))
        {
            return def?.DefaultEndpoint?.Trim().TrimEnd('/') ?? string.Empty;
        }

        if (protocol == ChatProtocol.Anthropic)
        {
            return NormalizeAnthropic(value, def);
        }

        if ((protocol == ChatProtocol.OpenAiResponses || protocol == ChatProtocol.OpenAiChatCompletions)
            && def?.Id == "deepseek"
            && !value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return value + "/v1";
        }

        return value;
    }

    private static string NormalizeAnthropic(string endpoint, LlmProviderDefinition? def)
    {
        if (endpoint.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        // Common mistake: OpenAI-style /v1 base while the gateway expects /anthropic.
        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint[..^3] + "/anthropic";
        }

        if (def?.Id == "moonshot")
        {
            return endpoint + "/anthropic";
        }

        return endpoint;
    }
}
