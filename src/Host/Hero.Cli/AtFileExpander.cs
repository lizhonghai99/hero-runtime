using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hero.Cli;

/// <summary>
/// Expands <c>@path</c> tokens to file contents under the working directory (no absolute / <c>..</c>).
/// </summary>
internal static partial class AtFileExpander
{
    private const int MaxBytesPerFile = 48_000;

    [GeneratedRegex(@"@([^\s]+)")]
    private static partial Regex AtRefRegex();

    public static string Expand(string input, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (string.IsNullOrEmpty(input) || !input.Contains('@', StringComparison.Ordinal))
        {
            return input;
        }

        var cwd = Path.GetFullPath(workingDirectory);
        var attachments = new List<(string Token, string Relative, string Body)>();

        foreach (Match match in AtRefRegex().Matches(input))
        {
            var token = match.Value;
            var rel = match.Groups[1].Value.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(rel))
            {
                continue;
            }

            if (attachments.Exists(a => a.Token.Equals(token, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!TryReadUnderCwd(cwd, rel, out var body, out var error))
            {
                attachments.Add((token, rel, $"ERROR: {error}"));
                continue;
            }

            attachments.Add((token, rel, body));
        }

        if (attachments.Count == 0)
        {
            return input;
        }

        var sb = new StringBuilder(input);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Attached files:");
        foreach (var (token, relative, body) in attachments)
        {
            sb.AppendLine();
            sb.Append("### ").Append(token).Append(" (").Append(relative).AppendLine(")");
            sb.AppendLine("```");
            sb.AppendLine(body);
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static bool TryReadUnderCwd(string cwd, string relative, out string body, out string error)
    {
        body = string.Empty;
        error = string.Empty;

        if (Path.IsPathRooted(relative)
            || relative.Contains("..", StringComparison.Ordinal)
            || relative.StartsWith('~'))
        {
            error = "path must be relative to the working directory (no absolute / .. / ~)";
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(cwd, relative));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!full.StartsWith(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !full.Equals(cwd, StringComparison.OrdinalIgnoreCase))
        {
            error = "path escapes the working directory";
            return false;
        }

        if (!File.Exists(full))
        {
            error = "file not found";
            return false;
        }

        try
        {
            var info = new FileInfo(full);
            if (info.Length > MaxBytesPerFile)
            {
                using var stream = File.OpenRead(full);
                var buffer = new byte[MaxBytesPerFile];
                var read = stream.Read(buffer, 0, buffer.Length);
                body = Encoding.UTF8.GetString(buffer, 0, read)
                       + $"\n… (truncated at {MaxBytesPerFile} bytes)";
            }
            else
            {
                body = File.ReadAllText(full);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
