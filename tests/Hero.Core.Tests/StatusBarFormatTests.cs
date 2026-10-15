using Hero.Core.Observability;

namespace Hero.Core.Tests;

/// <summary>
/// Mirrors StatusBar collapse rules (narrow = left only) without referencing the CLI exe.
/// </summary>
public sealed class StatusBarFormatTests
{
    [Fact]
    public void NarrowWidth_OmitsRightSide()
    {
        var usage = new SessionUsageSnapshot
        {
            InputTokens = 3800,
            OutputTokens = 100,
            LastContextTokens = 3800,
            EstimatedCostUsd = 0.001,
        };
        var model = new StatusModelInfo
        {
            Provider = "DeepSeek",
            Model = "deepseek-chat",
            ReasoningLevel = "none",
            MaxContextWindowTokens = 128_000,
            CompactionMode = "auto",
        };

        var narrow = FormatLikeStatusBar(usage, model, width: 60);
        var wide = FormatLikeStatusBar(usage, model, width: 120);

        Assert.DoesNotContain("DeepSeek", narrow, StringComparison.Ordinal);
        Assert.Contains("DeepSeek", wide, StringComparison.Ordinal);
        Assert.Contains('↑', narrow);
    }

    private static string FormatLikeStatusBar(SessionUsageSnapshot usage, StatusModelInfo model, int width)
    {
        var left =
            $"↑{usage.InputTokens} ↓{usage.OutputTokens} ${usage.EstimatedCostUsd:0.000} ({model.CompactionMode})";
        if (width < 72)
        {
            return left;
        }

        var right = $"({model.Provider}) {model.Model} · {model.ReasoningLevel}";
        return left + "   " + right;
    }
}
