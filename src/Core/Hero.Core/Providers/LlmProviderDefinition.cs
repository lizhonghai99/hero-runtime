using Hero.Core.Chat;

namespace Hero.Core.Providers;

/// <summary>Built-in LLM provider catalog entry.</summary>
public sealed class LlmProviderDefinition
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Default base URL for the chat client of <see cref="DefaultProtocol"/>.</summary>
    public required string DefaultEndpoint { get; init; }

    /// <summary>Default wire protocol when the user does not set <c>Chat:Protocol</c>.</summary>
    public ChatProtocol DefaultProtocol { get; init; } = ChatProtocol.OpenAiResponses;

    /// <summary>Absolute URL for <c>GET …/models</c> (OpenAI-compatible listing).</summary>
    public required string ModelsListUrl { get; init; }

    public required string ApiKeyEnvVars { get; init; }

    public string? EndpointEnvVars { get; init; }

    public string? ModelEnvVars { get; init; }

    public string? DefaultModelHint { get; init; }
}
