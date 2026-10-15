using System;
using System.IO;
using System.Text.Json;
using Hero.Core.Chat;
using Hero.Core.Providers;
using Hero.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Hero.Core.Options;

/// <summary>
/// Fills Chat options from: explicit config → ~/.hero-agent/settings.json → provider env vars.
/// API keys never come from settings files.
/// </summary>
internal sealed class ChatProviderOptionsConfigurator : IConfigureOptions<ChatProviderOptions>
{
    private readonly IConfiguration _configuration;

    public ChatProviderOptionsConfigurator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(ChatProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ApplySettingsFile(options);
        ApplyEnvAndConfig(options);
        Normalize(options);
    }

    private void ApplySettingsFile(ChatProviderOptions options)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hero-agent",
                "settings.json");
            SettingsDocument? doc = null;
            if (File.Exists(path))
            {
                doc = JsonSerializer.Deserialize<SettingsDocument>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            else
            {
                // one-shot read of legacy providers.json shape
                var legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".hero-agent",
                    "providers.json");
                if (File.Exists(legacyPath))
                {
                    var legacy = JsonSerializer.Deserialize<ProviderStoreDocument>(
                        File.ReadAllText(legacyPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (legacy is not null)
                    {
                        doc = new SettingsDocument { DefaultProvider = legacy.ActiveProviderId };
                        if (!string.IsNullOrWhiteSpace(legacy.ActiveProviderId)
                            && legacy.Providers.TryGetValue(legacy.ActiveProviderId, out var entry)
                            && entry is not null)
                        {
                            doc.DefaultModel = entry.Model;
                            doc.DefaultEndpoint = entry.Endpoint;
                        }
                    }
                }
            }

            if (doc is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(options.ProviderId) && !string.IsNullOrWhiteSpace(doc.DefaultProvider))
            {
                options.ProviderId = doc.DefaultProvider;
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint) && !string.IsNullOrWhiteSpace(doc.DefaultEndpoint))
            {
                options.Endpoint = doc.DefaultEndpoint;
            }

            if (string.IsNullOrWhiteSpace(options.Model) && !string.IsNullOrWhiteSpace(doc.DefaultModel))
            {
                options.Model = doc.DefaultModel;
            }

            if (string.IsNullOrWhiteSpace(options.Protocol) && !string.IsNullOrWhiteSpace(doc.DefaultProtocol))
            {
                options.Protocol = doc.DefaultProtocol;
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void ApplyEnvAndConfig(ChatProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProviderId))
        {
            options.ProviderId = FirstNonEmpty(_configuration["Chat:ProviderId"]) ?? string.Empty;
        }

        var def = LlmProviderCatalog.Find(options.ProviderId)
            ?? InferProviderFromEndpoint(options.Endpoint)
            ?? InferProviderFromEnv();

        if (def is not null && string.IsNullOrWhiteSpace(options.ProviderId))
        {
            options.ProviderId = def.Id;
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            options.Endpoint = FirstNonEmpty(
                _configuration["Chat:Endpoint"],
                def is null ? null : LlmProviderCatalog.ReadEnv(def.EndpointEnvVars),
                def?.DefaultEndpoint) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            // Never fall back to another provider's key (e.g. DeepSeek → Moonshot).
            options.ApiKey = FirstNonEmpty(
                _configuration["Chat:ApiKey"],
                def is null ? null : LlmProviderCatalog.ReadEnv(def.ApiKeyEnvVars)) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            options.Model = FirstNonEmpty(
                _configuration["Chat:Model"],
                def is null ? null : LlmProviderCatalog.ReadEnv(def.ModelEnvVars),
                def?.DefaultModelHint) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.Protocol))
        {
            options.Protocol = FirstNonEmpty(_configuration["Chat:Protocol"]) ?? string.Empty;
        }
    }

    private static void Normalize(ChatProviderOptions options)
    {
        var def = LlmProviderCatalog.Find(options.ProviderId)
            ?? InferProviderFromEndpoint(options.Endpoint);

        if (def is not null)
        {
            if (string.IsNullOrWhiteSpace(options.ProviderId))
            {
                options.ProviderId = def.Id;
            }

            options.Provider = def.DisplayName;
        }
        else if (string.IsNullOrWhiteSpace(options.Provider)
                 || string.Equals(options.Provider, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            options.Provider = "OpenAICompatible";
        }

        var protocol = ChatProtocolParser.Resolve(
            options.Protocol,
            def?.DefaultProtocol,
            options.Endpoint);

        options.Protocol = ChatProtocolParser.ToConfigString(protocol);
        options.Endpoint = ChatEndpointNormalizer.Normalize(options.Endpoint, protocol, def);
    }

    private static LlmProviderDefinition? InferProviderFromEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        if (endpoint.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderCatalog.Find("deepseek");
        }

        if (endpoint.Contains("moonshot", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("kimi", StringComparison.OrdinalIgnoreCase))
        {
            return LlmProviderCatalog.Find("moonshot");
        }

        return null;
    }

    private static LlmProviderDefinition? InferProviderFromEnv()
    {
        foreach (var def in LlmProviderCatalog.All)
        {
            if (!string.IsNullOrWhiteSpace(LlmProviderCatalog.ReadEnv(def.ApiKeyEnvVars)))
            {
                return def;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
