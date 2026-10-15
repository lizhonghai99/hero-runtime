using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Hero.Core.Convention;

namespace Hero.Plugins.Mcp;

/// <summary>
/// Loads and merges project + user <c>mcp.json</c> (project wins on same server name).
/// </summary>
public static class McpConfigLoader
{
    public const string FileName = "mcp.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string ProjectConfigPath(string workingDirectory) =>
        Path.Combine(ConventionPaths.ProjectRoot(workingDirectory), FileName);

    public static string UserConfigPath =>
        Path.Combine(ConventionPaths.UserRoot, FileName);

    /// <summary>
    /// Merge user then project (when allowed). Missing / invalid files are ignored (empty result).
    /// </summary>
    /// <param name="userConfigPath">Override for tests; default is <see cref="UserConfigPath"/>.</param>
    /// <param name="includeProjectConfig">
    /// When false, only user-level config is loaded (project mcp.json requires trust).
    /// </param>
    public static McpConfigDocument LoadMerged(
        string workingDirectory,
        string? userConfigPath = null,
        bool includeProjectConfig = true)
    {
        var merged = new McpConfigDocument();

        TryMergeFile(userConfigPath ?? UserConfigPath, merged);
        if (includeProjectConfig)
        {
            TryMergeFile(ProjectConfigPath(workingDirectory), merged);
        }

        return merged;
    }

    public static McpConfigDocument? TryLoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new McpConfigDocument();
            }

            return JsonSerializer.Deserialize<McpConfigDocument>(json, JsonOptions)
                   ?? new McpConfigDocument();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string FormatToolName(string serverName, string toolName)
    {
        var server = SanitizeSegment(serverName);
        var tool = SanitizeSegment(toolName);
        return $"mcp_{server}_{tool}";
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "x";
        }

        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                chars[i] = '_';
            }
        }

        var s = new string(chars);
        while (s.Contains("__", StringComparison.Ordinal))
        {
            s = s.Replace("__", "_", StringComparison.Ordinal);
        }

        return s.Trim('_').ToLowerInvariant() switch
        {
            "" => "x",
            var v => v,
        };
    }

    private static void TryMergeFile(string path, McpConfigDocument target)
    {
        var doc = TryLoadFile(path);
        if (doc?.McpServers is null || doc.McpServers.Count == 0)
        {
            return;
        }

        foreach (var (name, cfg) in doc.McpServers)
        {
            if (string.IsNullOrWhiteSpace(name) || cfg is null || !cfg.IsConfigured)
            {
                continue;
            }

            target.McpServers[name.Trim()] = cfg;
        }
    }
}
