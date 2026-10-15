using System;
using System.ClientModel;
using Anthropic;
using Anthropic.Core;
using Hero.Core.Options;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Responses;

namespace Hero.Core.Chat;

/// <summary>
/// Builds <see cref="IChatClient"/> for the configured <see cref="ChatProtocol"/>.
/// </summary>
internal static class ChatClientFactory
{
    public static IChatClient Create(ChatProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Chat is not configured. Set Chat:Endpoint / Chat:Model / Chat:ApiKey.");
        }

        var protocol = ChatProtocolParser.Resolve(
            options.Protocol,
            providerDefault: null,
            options.Endpoint);

        return protocol switch
        {
            ChatProtocol.Anthropic => CreateAnthropic(options),
            ChatProtocol.OpenAiChatCompletions => CreateOpenAiChatCompletions(options),
            _ => CreateOpenAiResponses(options),
        };
    }

    private static IChatClient CreateOpenAiResponses(ChatProviderOptions options)
    {
        var openAi = CreateOpenAiClient(options);

        // store=false: many OpenAI-compatible gateways do not honor previous_response_id.
        // Stateless Responses + local ChatHistoryProvider keeps multi-turn correct.
        return openAi.GetResponsesClient().AsIChatClientWithStoredOutputDisabled(options.Model);
    }

    private static IChatClient CreateOpenAiChatCompletions(ChatProviderOptions options)
    {
        var openAi = CreateOpenAiClient(options);
        return openAi.GetChatClient(options.Model).AsIChatClient();
    }

    private static OpenAIClient CreateOpenAiClient(ChatProviderOptions options)
    {
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Endpoint),
            // Avoid indefinite hangs when the provider stalls mid-stream.
            NetworkTimeout = TimeSpan.FromMinutes(2),
        };

        return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
    }

    private static IChatClient CreateAnthropic(ChatProviderOptions options)
    {
        var client = new AnthropicClient(new ClientOptions
        {
            ApiKey = options.ApiKey,
            BaseUrl = options.Endpoint,
        });

        return client.AsIChatClient(options.Model);
    }
}
