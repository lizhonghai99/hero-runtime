using System;
using System.Collections.Generic;
using System.Text;

namespace Hero.Core.Plugins;

/// <summary>Deterministic ASCII table for non-TTY / tests (no ANSI).</summary>
public static class PlainTextTable
{
    public static string Format(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (headers.Count == 0)
        {
            return string.Empty;
        }

        var widths = new int[headers.Count];
        for (var c = 0; c < headers.Count; c++)
        {
            widths[c] = headers[c]?.Length ?? 0;
        }

        foreach (var row in rows)
        {
            for (var c = 0; c < headers.Count; c++)
            {
                var cell = c < row.Count ? row[c] ?? string.Empty : string.Empty;
                if (cell.Length > widths[c])
                {
                    widths[c] = cell.Length;
                }
            }
        }

        var sb = new StringBuilder();
        AppendRow(sb, headers, widths);
        AppendSeparator(sb, widths);
        foreach (var row in rows)
        {
            var cells = new string[headers.Count];
            for (var c = 0; c < headers.Count; c++)
            {
                cells[c] = c < row.Count ? row[c] ?? string.Empty : string.Empty;
            }

            AppendRow(sb, cells, widths);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> cells, int[] widths)
    {
        for (var c = 0; c < widths.Length; c++)
        {
            if (c > 0)
            {
                sb.Append("  ");
            }

            var cell = c < cells.Count ? cells[c] ?? string.Empty : string.Empty;
            sb.Append(cell.PadRight(widths[c]));
        }

        sb.AppendLine();
    }

    private static void AppendSeparator(StringBuilder sb, int[] widths)
    {
        for (var c = 0; c < widths.Length; c++)
        {
            if (c > 0)
            {
                sb.Append("  ");
            }

            sb.Append(new string('-', Math.Max(1, widths[c])));
        }

        sb.AppendLine();
    }

    public static IReadOnlyList<IReadOnlyList<string>> Rows(params IReadOnlyList<string>[] rows) => rows;
}
