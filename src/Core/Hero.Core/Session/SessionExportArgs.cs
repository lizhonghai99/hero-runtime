using System;

namespace Hero.Core.Session;

/// <summary>Parse <c>/export [--html] [path]</c>.</summary>
public static class SessionExportArgs
{
    public static (bool Html, string? Path) Parse(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var html = false;
        string? path = null;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] is "--html" or "-html")
            {
                html = true;
                continue;
            }

            path = string.IsNullOrWhiteSpace(path) ? tokens[i] : path + " " + tokens[i];
        }

        return (html, path);
    }
}
