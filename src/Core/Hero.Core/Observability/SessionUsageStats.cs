namespace Hero.Core.Observability;

/// <summary>
/// Cumulative token / cost stats for the current process session (Pi-style status bar left side).
/// </summary>
public sealed class SessionUsageStats
{
    private readonly object _gate = new();

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public long TotalTokens { get; private set; }

    public long CachedInputTokens { get; private set; }

    /// <summary>Last reported input tokens — proxy for current context occupancy.</summary>
    public long LastContextTokens { get; private set; }

    public double EstimatedCostUsd { get; private set; }

    public void Add(TokenUsageDelta delta, TokenPricing? pricing = null)
    {
        lock (_gate)
        {
            InputTokens += delta.InputTokens;
            OutputTokens += delta.OutputTokens;
            TotalTokens += delta.TotalTokens > 0
                ? delta.TotalTokens
                : delta.InputTokens + delta.OutputTokens;
            CachedInputTokens += delta.CachedInputTokens;

            if (delta.InputTokens > 0)
            {
                LastContextTokens = delta.InputTokens;
            }
            else if (delta.TotalTokens > 0)
            {
                LastContextTokens = delta.TotalTokens;
            }

            if (pricing is not null)
            {
                EstimatedCostUsd += pricing.EstimateUsd(delta);
            }
        }
    }

    public SessionUsageSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new SessionUsageSnapshot
            {
                InputTokens = InputTokens,
                OutputTokens = OutputTokens,
                TotalTokens = TotalTokens,
                CachedInputTokens = CachedInputTokens,
                LastContextTokens = LastContextTokens,
                EstimatedCostUsd = EstimatedCostUsd,
            };
        }
    }
}

public sealed class SessionUsageSnapshot
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long LastContextTokens { get; init; }

    public double EstimatedCostUsd { get; init; }

    public double? CacheHitPercent =>
        InputTokens > 0 ? CachedInputTokens * 100.0 / InputTokens : null;
}

/// <summary>Optional USD rates per 1M tokens for status-bar cost.</summary>
public sealed class TokenPricing
{
    public double? InputUsdPer1M { get; init; }

    public double? OutputUsdPer1M { get; init; }

    public double? CachedInputUsdPer1M { get; init; }

    public bool HasAny =>
        InputUsdPer1M is > 0 || OutputUsdPer1M is > 0 || CachedInputUsdPer1M is > 0;

    public double EstimateUsd(TokenUsageDelta delta)
    {
        double cost = 0;
        if (InputUsdPer1M is > 0)
        {
            // Cached rate set: (input - cached) @ input + cached @ cached.
            // Cached rate unset: full input @ input (do not drop cached tokens from the bill).
            // cached > input (bad data): charge full input @ input only — no negative / double count.
            if (CachedInputUsdPer1M is > 0
                && delta.CachedInputTokens > 0
                && delta.CachedInputTokens <= delta.InputTokens)
            {
                var billableInput = delta.InputTokens - delta.CachedInputTokens;
                cost += billableInput / 1_000_000.0 * InputUsdPer1M.Value;
                cost += delta.CachedInputTokens / 1_000_000.0 * CachedInputUsdPer1M.Value;
            }
            else
            {
                cost += delta.InputTokens / 1_000_000.0 * InputUsdPer1M.Value;
            }
        }

        if (OutputUsdPer1M is > 0)
        {
            cost += delta.OutputTokens / 1_000_000.0 * OutputUsdPer1M.Value;
        }

        return cost;
    }
}
