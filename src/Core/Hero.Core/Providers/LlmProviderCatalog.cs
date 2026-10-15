using System;
using System.Collections.Generic;
using System.Linq;
using Hero.Core.Chat;

namespace Hero.Core.Providers;

public static class LlmProviderCatalog
{
    public static IReadOnlyList<LlmProviderDefinition> All { get; } =
    [
        new LlmProviderDefinition
        {
            Id = "deepseek",
            DisplayName = "DeepSeek",
            DefaultEndpoint = "https://api.deepseek.com/v1",
            // Responses with store=false (see ChatClientFactory) + TurnBoundChatClient.
            DefaultProtocol = ChatProtocol.OpenAiResponses,
            ModelsListUrl = "https://api.deepseek.com/models",
            ApiKeyEnvVars = "DEEPSEEK_API_KEY,DeepSeek-API-Key,DeepSeek_API_Key",
            EndpointEnvVars = "DEEPSEEK_END_POINT,DEEPSEEK_ENDPOINT,DeepSeek-End-Point",
            ModelEnvVars = "DEEPSEEK_MODEL,DeepSeek-Model",
            DefaultModelHint = "deepseek-flash",
        },
        new LlmProviderDefinition
        {
            Id = "moonshot",
            DisplayName = "Moonshot (Kimi)",
            // Anthropic Messages gateway (not OpenAI /v1 Responses).
            DefaultEndpoint = "https://api.moonshot.cn/anthropic",
            DefaultProtocol = ChatProtocol.Anthropic,
            ModelsListUrl = "https://api.moonshot.cn/v1/models",
            ApiKeyEnvVars = "MOONSHOT_API_KEY,KIMI_API_KEY",
            EndpointEnvVars = "MOONSHOT_END_POINT,MOONSHOT_BASE_URL,MOONSHOT_ENDPOINT",
            ModelEnvVars = "MOONSHOT_MODEL,KIMI_MODEL",
            DefaultModelHint = "kimi-k2.6",
        },
    ];

    public static LlmProviderDefinition? Find(string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
        {
            return null;
        }

        var key = idOrName.Trim();
        return All.FirstOrDefault(p =>
            p.Id.Equals(key, StringComparison.OrdinalIgnoreCase)
            || p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase)
            || (p.Id == "moonshot"
                && (key.Contains("moonshot", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("kimi", StringComparison.OrdinalIgnoreCase)))
            || (key.Contains("deepseek", StringComparison.OrdinalIgnoreCase) && p.Id == "deepseek"));
    }

    public static string? ReadEnv(string? csvNames)
    {
        if (string.IsNullOrWhiteSpace(csvNames))
        {
            return null;
        }

        foreach (var name in csvNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
