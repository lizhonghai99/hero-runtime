using System;
using System.IO;
using Hero.Core.Options;

namespace Hero.Core.Tools;

/// <summary>
/// Shared primitive for plugins: picks which shell binary MAF LocalShellExecutor should use.
/// MAF default (null): Windows pwsh→powershell, Linux/macOS bash→sh.
/// Extra Hero rule: Windows with neither PowerShell → fall back to cmd.
/// </summary>
public static class HostShellSelector
{
    /// <summary>
    /// Returns an explicit shell name for LocalShellExecutorOptions.Shell, or null for MAF auto-resolve.
    /// </summary>
    public static string? Select(HeroOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Shell))
        {
            return options.Shell.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable("HERO_SHELL")
            ?? Environment.GetEnvironmentVariable("AGENT_FRAMEWORK_SHELL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        if (OperatingSystem.IsWindows() && !IsPowerShellAvailable())
        {
            return "cmd";
        }

        return null;
    }

    public static string DescribeSelection(string? shellOverride)
    {
        if (!string.IsNullOrWhiteSpace(shellOverride))
        {
            return $"explicit:{shellOverride}";
        }

        if (OperatingSystem.IsWindows())
        {
            return IsPowerShellAvailable()
                ? "auto:Windows pwsh→powershell"
                : "auto:Windows cmd (no PowerShell on PATH)";
        }

        return "auto:POSIX bash→sh";
    }

    private static bool IsPowerShellAvailable()
    {
        return FindOnPath("pwsh.exe") is not null
            || FindOnPath("pwsh") is not null
            || FindOnPath("powershell.exe") is not null
            || FindOnPath("powershell") is not null;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore invalid PATH entries
            }
        }

        return null;
    }
}
