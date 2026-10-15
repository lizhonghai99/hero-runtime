using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace Hero.Cli;

/// <summary>Compact startup / session banner for the REPL (delegates colors to <see cref="ConsoleRenderer"/>).</summary>
internal static class BannerPrinter
{
    public static void EnsureUtf8Console()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // redirected / non-console hosts
        }
        catch (ArgumentException)
        {
        }
    }

    public static void WriteTitle(string version) => ConsoleRenderer.Current.WriteTitle(version);

    public static void WriteKv(string key, string value) => ConsoleRenderer.Current.WriteKv(key, value);

    public static void WriteHint(string text) => ConsoleRenderer.Current.WriteHint(text);

    public static string TruncatePath(string path, int maxWidth)
    {
        if (string.IsNullOrEmpty(path) || maxWidth <= 4 || DisplayWidth(path) <= maxWidth)
        {
            return path;
        }

        const string ellipsis = "…";
        var budget = maxWidth - ellipsis.Length;
        var chars = path.AsSpan();
        var end = chars.Length;
        var width = 0;
        while (end > 0)
        {
            end = StepBack(chars, end, out var w);
            if (width + w > budget)
            {
                end = StepForward(chars, end);
                break;
            }

            width += w;
        }

        return ellipsis + chars[end..].ToString();
    }

    internal static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneDisplayWidth(rune);
        }

        return width;
    }

    private static int RuneDisplayWidth(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return 0;
        }

        // Rough CJK / fullwidth heuristic for console columns.
        if (rune.Value is >= 0x1100 and <= 0x115F
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF)
        {
            return 2;
        }

        return 1;
    }

    private static int StepBack(ReadOnlySpan<char> chars, int end, out int width)
    {
        var status = Rune.DecodeLastFromUtf16(chars[..end], out var rune, out var charsConsumed);
        if (status != OperationStatus.Done || charsConsumed <= 0)
        {
            width = 1;
            return Math.Max(0, end - 1);
        }

        width = RuneDisplayWidth(rune);
        return end - charsConsumed;
    }

    private static int StepForward(ReadOnlySpan<char> chars, int index)
    {
        if (index >= chars.Length)
        {
            return index;
        }

        var status = Rune.DecodeFromUtf16(chars[index..], out _, out var charsConsumed);
        if (status != OperationStatus.Done || charsConsumed <= 0)
        {
            return index + 1;
        }

        return index + charsConsumed;
    }
}
