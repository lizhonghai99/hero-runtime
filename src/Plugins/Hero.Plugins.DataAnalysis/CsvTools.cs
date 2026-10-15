using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Hero.Core.Tools;
using Microsoft.Extensions.AI;

namespace Hero.Plugins.DataAnalysis;

/// <summary>
/// Demo CSV helpers confined to the working directory.
/// </summary>
internal static class CsvTools
{
    private static readonly string[] DataExtensions = [".csv", ".tsv", ".json"];

    public static IReadOnlyList<AITool> CreateAll(string workingDirectory)
    {
        var root = Path.GetFullPath(workingDirectory);
        return
        [
            AIFunctionFactory.Create(
                ListDataFilesCore,
                name: "list_data_files",
                description: "List .csv / .tsv / .json files under the working directory (recursive, skips bin/obj)."),
            AIFunctionFactory.Create(
                SummarizeCsvCore,
                name: "summarize_csv",
                description: "Summarize a CSV/TSV: columns, row count, and basic numeric stats."),
            AIFunctionFactory.Create(
                PeekCsvCore,
                name: "peek_csv",
                description: "Preview the first N rows of a CSV/TSV as plain text."),
        ];

        string ListDataFilesCore() => ListDataFiles(root);

        string SummarizeCsvCore(
            [Description("Relative path to a .csv or .tsv file")] string path,
            [Description("Max data rows to scan for stats (default 5000)")] int maxRows = 5000)
            => SummarizeCsv(root, path, maxRows);

        string PeekCsvCore(
            [Description("Relative path to a .csv or .tsv file")] string path,
            [Description("Number of data rows to preview (default 5, max 30)")] int rows = 5)
            => PeekCsv(root, path, rows);
    }

    private static string ListDataFiles(string root)
    {
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p =>
            {
                var ext = Path.GetExtension(p);
                if (!DataExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }

                var rel = Path.GetRelativePath(root, p);
                return !rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !rel.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
            })
            .Select(p => Path.GetRelativePath(root, p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();

        if (files.Count == 0)
        {
            return "(no .csv/.tsv/.json data files found under working directory)";
        }

        return string.Join(Environment.NewLine, files);
    }

    private static string SummarizeCsv(string root, string path, int maxRows)
    {
        var full = ResolveUnderRoot(root, path);
        var sep = Path.GetExtension(full).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var limit = maxRows > 0 ? maxRows : 5000;

        using var reader = new StreamReader(full, Encoding.UTF8);
        var headerLine = reader.ReadLine()
            ?? throw new InvalidOperationException("File is empty.");
        var headers = SplitLine(headerLine, sep);
        if (headers.Count == 0)
        {
            throw new InvalidOperationException("No header columns.");
        }

        var numeric = headers.Select(_ => new List<double>()).ToArray();
        var nonEmpty = new int[headers.Count];
        var rows = 0;

        while (rows < limit && reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitLine(line, sep);
            rows++;
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = i < cells.Count ? cells[i].Trim() : string.Empty;
                if (cell.Length == 0)
                {
                    continue;
                }

                nonEmpty[i]++;
                if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
                    || double.TryParse(cell, NumberStyles.Float, CultureInfo.CurrentCulture, out n))
                {
                    numeric[i].Add(n);
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"file: {Path.GetRelativePath(root, full)}");
        sb.AppendLine($"separator: {(sep == '\t' ? "TAB" : "COMMA")}");
        sb.AppendLine($"rows_scanned: {rows}{(reader.EndOfStream ? "" : "+ (truncated)")}");
        sb.AppendLine($"columns: {headers.Count}");
        sb.AppendLine();

        for (var i = 0; i < headers.Count; i++)
        {
            sb.Append("- ").Append(headers[i]);
            sb.Append(" | non_empty=").Append(nonEmpty[i]);
            if (numeric[i].Count >= Math.Max(3, (int)(rows * 0.5)))
            {
                sb.Append(" | numeric min=")
                    .Append(numeric[i].Min().ToString("G6", CultureInfo.InvariantCulture))
                    .Append(" max=")
                    .Append(numeric[i].Max().ToString("G6", CultureInfo.InvariantCulture))
                    .Append(" avg=")
                    .Append(numeric[i].Average().ToString("G6", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string PeekCsv(string root, string path, int rows)
    {
        var full = ResolveUnderRoot(root, path);
        var take = Math.Clamp(rows > 0 ? rows : 5, 1, 30);

        using var reader = new StreamReader(full, Encoding.UTF8);
        var sb = new StringBuilder();
        var header = reader.ReadLine();
        if (header is null)
        {
            return "(empty file)";
        }

        sb.AppendLine(header);
        var n = 0;
        while (n < take && reader.ReadLine() is { } line)
        {
            sb.AppendLine(line);
            n++;
        }

        return sb.ToString();
    }

    private static string ResolveUnderRoot(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            throw new ArgumentException("path is required.", nameof(relative));
        }

        var full = PathGuard.ResolveUnderRoot(root, relative);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"File not found: {relative}", full);
        }

        return full;
    }

    private static List<string> SplitLine(string line, char sep)
    {
        // Minimal CSV: split on separator; strips surrounding quotes. Good enough for demo.
        var parts = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == sep && !inQuotes)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        parts.Add(sb.ToString());
        return parts;
    }
}
