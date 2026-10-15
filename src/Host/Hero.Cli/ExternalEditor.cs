using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Cli;

/// <summary>Opens <c>$VISUAL</c> / <c>$EDITOR</c> / Notepad / nano for a long prompt (Pi Ctrl+G).</summary>
internal static class ExternalEditor
{
    public static async Task<string?> EditAsync(string? initial, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hi-prompt-{Guid.NewGuid():N}.md");
        try
        {
            await File.WriteAllTextAsync(path, initial ?? string.Empty, cancellationToken).ConfigureAwait(false);

            var (fileName, args) = ResolveEditor(path);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = OperatingSystem.IsWindows() && IsBareNotepad(fileName),
                },
            };

            if (!process.Start())
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (!File.Exists(path))
            {
                return null;
            }

            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // temp cleanup best-effort
            }
        }
    }

    private static (string FileName, string Args) ResolveEditor(string path)
    {
        var quoted = Quote(path);
        var visual = Environment.GetEnvironmentVariable("VISUAL");
        if (!string.IsNullOrWhiteSpace(visual))
        {
            return SplitCommand(visual.Trim(), quoted);
        }

        var editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrWhiteSpace(editor))
        {
            return SplitCommand(editor.Trim(), quoted);
        }

        if (OperatingSystem.IsWindows())
        {
            return ("notepad.exe", quoted);
        }

        return ("nano", quoted);
    }

    private static (string FileName, string Args) SplitCommand(string command, string fileArg)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (parts[0], fileArg);
        }

        return (parts[0], parts[1] + " " + fileArg);
    }

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static bool IsBareNotepad(string fileName) =>
        fileName.Equals("notepad", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase);
}
