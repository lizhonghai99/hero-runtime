using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Providers;

public sealed class RemoteModelInfo
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    public string? OwnedBy { get; init; }

    public string Display => string.IsNullOrWhiteSpace(Name) || Name == Id ? Id : $"{Id}  ({Name})";
}

/// <summary>Fetches model lists via OpenAI-compatible <c>GET /models</c>.</summary>
public sealed class OpenAiCompatibleModelCatalog
{
    private readonly HttpClient _http;

    public OpenAiCompatibleModelCatalog(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<IReadOnlyList<RemoteModelInfo>> ListModelsAsync(
        LlmProviderDefinition provider,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Get, provider.ModelsListUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"List models failed ({(int)response.StatusCode} {response.ReasonPhrase}) from {provider.DisplayName}: {Truncate(body, 240)}");
        }

        return ParseModelsJson(body);
    }

    internal static IReadOnlyList<RemoteModelInfo> ParseModelsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Models response missing data[] array.");
        }

        var list = new List<RemoteModelInfo>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string? name = null;
            if (item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                name = nameEl.GetString();
            }

            string? owned = null;
            if (item.TryGetProperty("owned_by", out var ownedEl) && ownedEl.ValueKind == JsonValueKind.String)
            {
                owned = ownedEl.GetString();
            }

            list.Add(new RemoteModelInfo { Id = id!, Name = name, OwnedBy = owned });
        }

        return list
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
