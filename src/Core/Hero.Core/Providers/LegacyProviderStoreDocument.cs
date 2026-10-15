using System;
using System.Collections.Generic;

namespace Hero.Core.Providers;

/// <summary>Legacy ~/.hero-agent/providers.json shape (read-only migration into settings.json).</summary>
public sealed class ProviderStoreDocument
{
    public string? ActiveProviderId { get; set; }

    public Dictionary<string, ProviderCredentialEntry> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderCredentialEntry
{
    public string? Endpoint { get; set; }

    public string? Model { get; set; }
}
