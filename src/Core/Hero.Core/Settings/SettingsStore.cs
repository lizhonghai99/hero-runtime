using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Providers;

namespace Hero.Core.Settings;

/// <summary>Load/save ~/.hero-agent/settings.json. Migrates legacy providers.json once.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly string _legacyProvidersPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hero-agent",
                "settings.json"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hero-agent",
                "providers.json"))
    {
    }

    public SettingsStore(string path, string legacyProvidersPath)
    {
        _path = path;
        _legacyProvidersPath = legacyProvidersPath;
    }

    public string FilePath => _path;

    public async Task<SettingsDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                await using var stream = File.OpenRead(_path);
                var doc = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return doc ?? new SettingsDocument();
            }

            return TryMigrateLegacyProviders() ?? new SettingsDocument();
        }
        catch (JsonException)
        {
            return new SettingsDocument();
        }
        catch (IOException)
        {
            return new SettingsDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new SettingsDocument();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SettingsDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SettingsDocument? TryMigrateLegacyProviders()
    {
        try
        {
            if (!File.Exists(_legacyProvidersPath))
            {
                return null;
            }

            var json = File.ReadAllText(_legacyProvidersPath);
            var legacy = JsonSerializer.Deserialize<ProviderStoreDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (legacy is null || string.IsNullOrWhiteSpace(legacy.ActiveProviderId))
            {
                return null;
            }

            var migrated = new SettingsDocument
            {
                DefaultProvider = legacy.ActiveProviderId,
            };

            if (legacy.Providers.TryGetValue(legacy.ActiveProviderId, out var entry) && entry is not null)
            {
                migrated.DefaultModel = entry.Model;
                migrated.DefaultEndpoint = entry.Endpoint;
            }

            return migrated;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
