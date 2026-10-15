using System;
using System.Collections.Generic;
using System.IO;
using Hero.Core.Plugins;

namespace Hero.Cli;

/// <summary>
/// Single entry for classic CLI output (print / redirected REPL).
/// No Spectre — plain writers + optional console colors on a TTY.
/// </summary>
internal sealed class ConsoleRenderer
{
    private static ConsoleRenderer? _current;

    private readonly bool _jsonMode;
    private readonly bool _stdoutRich;
    private readonly bool _stderrRich;
    private readonly TextWriter _stdoutWriter;
    private readonly TextWriter _stderrWriter;

    private ConsoleRenderer(
        bool jsonMode,
        bool stdoutRich,
        bool stderrRich,
        TextWriter stdoutWriter,
        TextWriter stderrWriter)
    {
        _jsonMode = jsonMode;
        _stdoutRich = stdoutRich;
        _stderrRich = stderrRich;
        _stdoutWriter = stdoutWriter;
        _stderrWriter = stderrWriter;
    }

    public static ConsoleRenderer Current =>
        _current ?? throw new InvalidOperationException("ConsoleRenderer.Initialize was not called.");

    public bool JsonMode => _jsonMode;

    public bool StdoutRich => _stdoutRich;

    /// <summary>Test / host bootstrap.</summary>
    public static ConsoleRenderer Initialize(
        bool jsonMode,
        bool? stdoutRedirected = null,
        bool? stderrRedirected = null,
        TextWriter? stdoutWriter = null,
        TextWriter? stderrWriter = null)
    {
        var outRedirected = stdoutRedirected ?? Console.IsOutputRedirected;
        var errRedirected = stderrRedirected ?? Console.IsErrorRedirected;

        var stdoutRich = !jsonMode && !outRedirected;
        var stderrRich = !errRedirected;

        _current = new ConsoleRenderer(
            jsonMode,
            stdoutRich,
            stderrRich,
            stdoutWriter ?? Console.Out,
            stderrWriter ?? Console.Error);
        return _current;
    }

    /// <summary>Reset for tests.</summary>
    internal static void ResetForTests() => _current = null;

    public void WriteLine(string text)
    {
        if (_jsonMode)
        {
            return;
        }

        _stdoutWriter.WriteLine(text);
    }

    public void Write(string text)
    {
        if (_jsonMode)
        {
            return;
        }

        _stdoutWriter.Write(text);
    }

    public void WriteStdoutRaw(string text) => _stdoutWriter.Write(text);

    public void WriteStdoutRawLine(string? text = null) => _stdoutWriter.WriteLine(text);

    public void WriteError(string text)
    {
        var line = text.StartsWith("error>", StringComparison.Ordinal) ? text : $"error> {text}";
        WriteColored(_stderrWriter, line, ConsoleColor.Red, _stderrRich);
    }

    public void WriteTool(string text)
    {
        var line = text.StartsWith("tool>", StringComparison.Ordinal) ? text : $"tool> {text}";
        WriteColored(_stderrWriter, line, ConsoleColor.DarkCyan, _stderrRich);
    }

    public void WriteApproval(string text)
    {
        var line = text.StartsWith("approval>", StringComparison.Ordinal) ? text : $"approval> {text}";
        WriteColored(_stderrWriter, line, ConsoleColor.Yellow, _stderrRich);
    }

    public void WriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (_jsonMode)
        {
            return;
        }

        _stdoutWriter.WriteLine(PlainTextTable.Format(headers, rows));
    }

    public void WriteKv(string key, string value)
    {
        if (_jsonMode)
        {
            return;
        }

        var truncated = BannerPrinter.TruncatePath(value, MaxValueWidth());
        _stdoutWriter.WriteLine($"{key,-8}{truncated}");
    }

    public void WriteHint(string text)
    {
        if (_jsonMode)
        {
            return;
        }

        WriteColored(_stdoutWriter, text, ConsoleColor.DarkGray, _stdoutRich);
    }

    public void WriteTitle(string version)
    {
        if (_jsonMode)
        {
            return;
        }

        _stdoutWriter.WriteLine($"hi — HERO {version}");
    }

    /// <summary>
    /// Non-streaming completed text block. Streaming chat stays plain — do not call per chunk.
    /// </summary>
    public void WriteCompletedBlock(string text)
    {
        if (_jsonMode || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _stdoutWriter.WriteLine(text);
    }

    /// <summary>Expose mode decision for unit tests.</summary>
    public static ConsoleRenderCapabilities Detect(
        bool jsonMode,
        bool stdoutRedirected,
        bool stderrRedirected) =>
        new(
            JsonMode: jsonMode,
            StdoutRich: !jsonMode && !stdoutRedirected,
            StderrRich: !stderrRedirected);

    private static void WriteColored(TextWriter writer, string text, ConsoleColor color, bool rich)
    {
        if (!rich || !ReferenceEquals(writer, Console.Out) && !ReferenceEquals(writer, Console.Error))
        {
            writer.WriteLine(text);
            return;
        }

        var prev = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            writer.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = prev;
        }
    }

    private static int MaxValueWidth()
    {
        try
        {
            return Math.Max(24, Console.WindowWidth - 10);
        }
        catch (IOException)
        {
            return 72;
        }
    }
}

internal readonly record struct ConsoleRenderCapabilities(bool JsonMode, bool StdoutRich, bool StderrRich);
