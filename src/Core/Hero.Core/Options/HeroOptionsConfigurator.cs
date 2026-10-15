using System;
using System.IO;
using System.Text.Json;
using Hero.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Hero.Core.Options;

/// <summary>Applies ~/.hero-agent/settings.json defaults onto <see cref="HeroOptions"/>.</summary>
internal sealed class HeroOptionsConfigurator : IConfigureOptions<HeroOptions>
{
    private readonly IConfiguration _configuration;

    public HeroOptionsConfigurator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(HeroOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Explicit Hero:* in config wins over settings.json.
        var reasoningFromConfig = _configuration["Hero:ReasoningLevel"];
        if (!string.IsNullOrWhiteSpace(reasoningFromConfig))
        {
            options.ReasoningLevel = reasoningFromConfig.Trim();
        }

        var approvalFromConfig = _configuration["Hero:ToolApproval"];
        if (!string.IsNullOrWhiteSpace(approvalFromConfig))
        {
            options.ToolApproval = approvalFromConfig.Trim().ToLowerInvariant();
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hero-agent",
                "settings.json");
            if (!File.Exists(path))
            {
                return;
            }

            var doc = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (doc is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(reasoningFromConfig)
                && !string.IsNullOrWhiteSpace(doc.DefaultThinkingLevel))
            {
                options.ReasoningLevel = Normalize(doc.DefaultThinkingLevel);
            }

            if (string.IsNullOrWhiteSpace(approvalFromConfig)
                && !string.IsNullOrWhiteSpace(doc.ToolApproval))
            {
                options.ToolApproval = doc.ToolApproval.Trim().ToLowerInvariant();
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

    private static string Normalize(string level) =>
        level.Trim().ToLowerInvariant() switch
        {
            "off" or "minimal" or "none" => "none",
            "low" => "low",
            "med" or "medium" => "medium",
            "high" => "high",
            _ => "none",
        };
}
