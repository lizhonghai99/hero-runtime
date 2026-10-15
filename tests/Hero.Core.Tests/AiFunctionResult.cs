using System.Text.Json;

namespace Hero.Core.Tests;

internal static class AiFunctionResult
{
    public static string AsString(object? raw) => raw switch
    {
        null => string.Empty,
        string s => s,
        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString() ?? string.Empty,
        JsonElement je => je.ToString(),
        _ => raw.ToString() ?? string.Empty,
    };
}
