using Microsoft.Extensions.AI;

namespace Hero.Core.Options;

/// <summary>
/// Shared parsing for Hero reasoning / thinking level strings.
/// </summary>
internal static class ReasoningLevelMapper
{
    /// <summary>
    /// Maps a user/config level to a canonical token.
    /// Returns <see langword="false"/> when the input was non-empty but unrecognized
    /// (caller should warn; <paramref name="normalized"/> is still <c>none</c>).
    /// </summary>
    public static bool TryNormalize(string? level, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            normalized = "none";
            return true;
        }

        switch (level.Trim().ToLowerInvariant())
        {
            case "off":
            case "minimal":
            case "none":
                normalized = "none";
                return true;
            case "low":
                normalized = "low";
                return true;
            case "med":
            case "medium":
                normalized = "medium";
                return true;
            case "high":
                normalized = "high";
                return true;
            default:
                normalized = "none";
                return false;
        }
    }

    public static ReasoningEffort ToEffort(string? level, out bool recognized)
    {
        recognized = TryNormalize(level, out var normalized);
        return normalized switch
        {
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            _ => ReasoningEffort.None,
        };
    }
}
