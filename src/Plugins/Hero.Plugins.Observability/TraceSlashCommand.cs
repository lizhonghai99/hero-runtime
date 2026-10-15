using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Observability;
using Hero.Core.Plugins;

namespace Hero.Plugins.Observability;

internal sealed class TraceSlashCommand : ISlashCommand
{
    private readonly ChatTraceStore _store;

    public TraceSlashCommand(ChatTraceStore store)
    {
        _store = store;
    }

    public string Name => "trace";

    public string Description =>
        "/trace [n] | /trace call <i> — model-call trajectory for a turn (1 = latest)";

    public Task<bool> TryHandleAsync(
        string line,
        ISlashCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0
            || !parts[0].Equals("/trace", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        try
        {
            if (parts.Length >= 3
                && parts[1].Equals("call", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var callIndex))
            {
                PrintCall(context, turnFromEnd: 1, callIndex);
                return Task.FromResult(true);
            }

            if (parts.Length >= 4
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var turnN)
                && parts[2].Equals("call", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out callIndex))
            {
                PrintCall(context, turnFromEnd: turnN, callIndex);
                return Task.FromResult(true);
            }

            var fromEnd = 1;
            if (parts.Length >= 2
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                fromEnd = n;
            }

            PrintTurn(context, fromEnd);
        }
        catch (Exception ex)
        {
            context.WriteError(ex.Message);
        }

        return Task.FromResult(true);
    }

    private void PrintTurn(ISlashCommandContext context, int fromEnd)
    {
        var turn = _store.GetTurnFromEnd(fromEnd);
        if (turn is null)
        {
            context.WriteLine("  (no traced turns yet — send a message first)");
            return;
        }

        context.WriteLine($"  turn[{fromEnd}] id={ShortId(turn.TurnId)}  calls={turn.Calls.Count}");
        if (turn.Calls.Count == 0)
        {
            context.WriteLine("  (no model calls recorded for this turn)");
            return;
        }

        // Surface the last user text actually sent to the model (multi-turn smoke check).
        var firstCall = turn.Calls[0];
        var lastUser = firstCall.Messages.LastOrDefault(m =>
            m.Role.Equals("User", StringComparison.OrdinalIgnoreCase)
            || m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        if (lastUser is not null)
        {
            var preview = lastUser.Text.Replace('\n', ' ');
            if (preview.Length > 80)
            {
                preview = preview[..80] + "…";
            }

            context.WriteLine($"  lastUser> {preview}");
        }

        var rows = new List<IReadOnlyList<string>>(turn.Calls.Count);
        foreach (var call in turn.Calls)
        {
            var ms = call.DurationMs is { } d ? $"{d:0}" : "?";
            var tools = call.ToolNames.Count == 0
                ? "0"
                : $"{call.ToolNames.Count} [{string.Join(", ", call.ToolNames.Take(6))}{(call.ToolNames.Count > 6 ? ", …" : "")}]";
            rows.Add(
            [
                string.IsNullOrWhiteSpace(call.AgentName) ? "hi" : call.AgentName!,
                $"#{call.CallIndex}",
                tools,
                $"{call.InputTokens}/{call.OutputTokens}",
                ms,
            ]);
        }

        context.WriteTable(["agent", "call", "tools", "in/out", "ms"], rows);

        foreach (var call in turn.Calls)
        {
            if (!string.IsNullOrWhiteSpace(call.Error))
            {
                context.WriteError($"call #{call.CallIndex}: {call.Error}");
            }
        }

        context.WriteLine("  tip> /trace call <i>  — message summaries for call #i on latest turn");
    }

    private void PrintCall(ISlashCommandContext context, int turnFromEnd, int callIndex)
    {
        var turn = _store.GetTurnFromEnd(turnFromEnd);
        if (turn is null)
        {
            context.WriteLine("  (no traced turns yet)");
            return;
        }

        var call = turn.Calls.FirstOrDefault(c => c.CallIndex == callIndex);
        if (call is null)
        {
            context.WriteLine($"  (call #{callIndex} not found on turn[{turnFromEnd}])");
            return;
        }

        context.WriteLine(
            $"  turn[{turnFromEnd}] call #{call.CallIndex}  msgs={call.Messages.Count}  tools={call.ToolNames.Count}");
        if (call.ToolNames.Count > 0)
        {
            context.WriteLine("  tools: " + string.Join(", ", call.ToolNames));
        }

        var i = 0;
        foreach (var msg in call.Messages)
        {
            i++;
            context.WriteLine($"  [{i}] {msg.Role} ({msg.CharCount} chars)");
            foreach (var line in msg.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
                         .Split('\n', StringSplitOptions.None)
                         .Take(12))
            {
                context.WriteLine("      " + line);
            }

            if (msg.Text.Count(c => c == '\n') >= 12 || msg.CharCount > msg.Text.Length)
            {
                context.WriteLine("      …");
            }
        }
    }

    private static string ShortId(string id) =>
        id.Length <= 8 ? id : id[..8];
}
