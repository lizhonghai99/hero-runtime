using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hero.Core.Convention;

namespace Hero.Plugins.Mcp;

/// <summary>
/// Persists trusted project <c>mcp.json</c> fingerprints under
/// <c>~/.hero-agent/trusted-mcp.json</c>.
/// </summary>
public sealed class McpProjectTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly object _gate = new();

    public McpProjectTrustStore()
        : this(Path.Combine(ConventionPaths.UserRoot, "trusted-mcp.json"))
    {
    }

    public McpProjectTrustStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public string FilePath => _path;

    public static string ComputeContentHash(string contents)
    {
        var bytes = Encoding.UTF8.GetBytes(contents ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string NormalizeConfigKey(string projectConfigPath) =>
        Path.GetFullPath(projectConfigPath);

    public bool IsTrusted(string projectConfigPath, string contentHash)
    {
        lock (_gate)
        {
            var doc = Load();
            var key = NormalizeConfigKey(projectConfigPath);
            return doc.Entries.TryGetValue(key, out var entry)
                && string.Equals(entry.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Trust(string projectConfigPath, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        lock (_gate)
        {
            var doc = Load();
            var key = NormalizeConfigKey(projectConfigPath);
            doc.Entries[key] = new TrustEntry
            {
                ContentHash = contentHash.Trim(),
                TrustedAtUtc = DateTimeOffset.UtcNow,
            };
            Save(doc);
        }
    }

    private TrustDocument Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new TrustDocument();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<TrustDocument>(json, JsonOptions) ?? new TrustDocument();
        }
        catch (IOException)
        {
            return new TrustDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new TrustDocument();
        }
        catch (JsonException)
        {
            return new TrustDocument();
        }
    }

    private void Save(TrustDocument document)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Temp + replace so a crash mid-write cannot leave a truncated trust file.
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var tempPath = _path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _path, overwrite: true);
    }

    private sealed class TrustDocument
    {
        public Dictionary<string, TrustEntry> Entries { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TrustEntry
    {
        public string ContentHash { get; set; } = string.Empty;

        public DateTimeOffset? TrustedAtUtc { get; set; }
    }
}
