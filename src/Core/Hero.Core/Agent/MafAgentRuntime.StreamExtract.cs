using System.Collections.Generic;
using Hero.Core.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Agent;

internal partial class MafAgentRuntime
{
    private static IEnumerable<TokenUsageDelta> ExtractUsage(AgentResponseUpdate update)
    {
        if (update.Contents is null || update.Contents.Count == 0)
        {
            yield break;
        }

        foreach (var content in update.Contents)
        {
            if (content is UsageContent { Details: { } details })
            {
                var delta = TokenUsageDelta.FromUsageDetails(details);
                if (delta is not null)
                {
                    yield return delta;
                }
            }
        }
    }

    private static IEnumerable<string> ExtractToolLines(AgentResponseUpdate update)
    {
        if (update.Contents is null || update.Contents.Count == 0)
        {
            yield break;
        }

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    var args = call.Arguments is null ? string.Empty : FormatArgs(call.Arguments);
                    yield return $"{call.Name}({args})";
                    break;
                case FunctionResultContent result:
                    var body = result.Result?.ToString() ?? string.Empty;
                    // Stream enough for the TUI/REPL to read; /trace keeps the full call.
                    yield return $"→ {Truncate(body, 8_000)}";
                    break;
            }
        }
    }

    private static string FormatArgs(IDictionary<string, object?> args)
    {
        var parts = new List<string>(args.Count);
        foreach (var pair in args)
        {
            parts.Add($"{pair.Key}={Truncate(pair.Value?.ToString() ?? string.Empty, 240)}");
        }

        return string.Join(", ", parts);
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }
}
