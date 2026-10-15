using System;

namespace Hero.Core.Chat;

/// <summary>Parse / format <see cref="ChatProtocol"/> config strings.</summary>
public static class ChatProtocolParser
{
    public const string OpenAiResponsesValue = "openai-responses";
    public const string OpenAiChatCompletionsValue = "openai-chat";
    public const string AnthropicValue = "anthropic";

    public static string ToConfigString(ChatProtocol protocol) =>
        protocol switch
        {
            ChatProtocol.Anthropic => AnthropicValue,
            ChatProtocol.OpenAiChatCompletions => OpenAiChatCompletionsValue,
            _ => OpenAiResponsesValue,
        };

    public static bool TryParse(string? value, out ChatProtocol protocol)
    {
        protocol = ChatProtocol.OpenAiResponses;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = value.Trim();
        if (key.Equals(OpenAiResponsesValue, StringComparison.OrdinalIgnoreCase)
            || key.Equals("responses", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ChatProtocol.OpenAiResponses;
            return true;
        }

        if (key.Equals(OpenAiChatCompletionsValue, StringComparison.OrdinalIgnoreCase)
            || key.Equals("chat", StringComparison.OrdinalIgnoreCase)
            || key.Equals("chat-completions", StringComparison.OrdinalIgnoreCase)
            || key.Equals("openai-chat-completions", StringComparison.OrdinalIgnoreCase)
            || key.Equals("completions", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ChatProtocol.OpenAiChatCompletions;
            return true;
        }

        // Legacy aliases: "openai" used to mean Responses; keep that for Azure/OpenAI hosts.
        if (key.Equals("openai", StringComparison.OrdinalIgnoreCase)
            || key.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ChatProtocol.OpenAiResponses;
            return true;
        }

        if (key.Equals(AnthropicValue, StringComparison.OrdinalIgnoreCase)
            || key.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || key.Equals("messages", StringComparison.OrdinalIgnoreCase)
            || key.Equals("anthropic-messages", StringComparison.OrdinalIgnoreCase))
        {
            protocol = ChatProtocol.Anthropic;
            return true;
        }

        return false;
    }

    public static ChatProtocol? ParseOrNull(string? value) =>
        TryParse(value, out var protocol) ? protocol : null;

    /// <summary>Infer protocol from a base URL (e.g. path contains <c>anthropic</c>).</summary>
    public static ChatProtocol? InferFromEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        if (endpoint.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return ChatProtocol.Anthropic;
        }

        return null;
    }

    /// <summary>
    /// Resolve effective protocol: explicit config → provider default → endpoint hint → Responses.
    /// </summary>
    public static ChatProtocol Resolve(
        string? explicitProtocol,
        ChatProtocol? providerDefault,
        string? endpoint)
    {
        if (TryParse(explicitProtocol, out var parsed))
        {
            return parsed;
        }

        if (providerDefault is { } fromProvider)
        {
            return fromProvider;
        }

        return InferFromEndpoint(endpoint) ?? ChatProtocol.OpenAiResponses;
    }
}
