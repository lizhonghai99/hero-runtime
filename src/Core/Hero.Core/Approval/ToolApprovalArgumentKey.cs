using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hero.Core.Approval;

/// <summary>
/// Stable serialization of tool arguments for "approve always with these args" keys.
/// </summary>
public static class ToolApprovalArgumentKey
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static string Compute(string toolName, IReadOnlyDictionary<string, object?>? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        if (arguments is not null)
        {
            foreach (var pair in arguments.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                payload[pair.Key] = Normalize(pair.Value);
            }
        }

        var argsJson = JsonSerializer.Serialize(payload, JsonOptions);
        return toolName.Trim() + "\n" + argsJson;
    }

    private static object? Normalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => el.GetRawText(),
            };
        }

        if (value is IDictionary<string, object?> nested)
        {
            var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in nested.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sorted[pair.Key] = Normalize(pair.Value);
            }

            return sorted;
        }

        return value;
    }
}
