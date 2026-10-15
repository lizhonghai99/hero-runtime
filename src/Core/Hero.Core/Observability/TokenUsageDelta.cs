using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Hero.Core.Observability;

/// <summary>
/// One usage report from the model (may arrive mid-turn or at turn end).
/// </summary>
public sealed class TokenUsageDelta
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public static TokenUsageDelta? FromUsageDetails(UsageDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        var input = details.InputTokenCount ?? 0;
        var output = details.OutputTokenCount ?? 0;
        var total = details.TotalTokenCount ?? (input + output);
        var cached = ReadCachedInput(details.AdditionalCounts);

        if (input == 0 && output == 0 && total == 0 && cached == 0)
        {
            return null;
        }

        return new TokenUsageDelta
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = total,
            CachedInputTokens = cached,
        };
    }

    private static long ReadCachedInput(AdditionalPropertiesDictionary<long>? counts)
    {
        if (counts is null || counts.Count == 0)
        {
            return 0;
        }

        foreach (var pair in counts)
        {
            if (pair.Key.Contains("cache", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("cached", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 0;
    }
}
