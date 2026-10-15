using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Cli;

/// <summary>Run a shell command in the current working directory for ! / !!.</summary>
internal static class LocalCommandRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        foreach (var attempt in ResolveShellAttempts())
        {
            var psi = new ProcessStartInfo
            {
                FileName = attempt.FileName,
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in attempt.Args)
            {
                psi.ArgumentList.Add(arg);
            }

            psi.ArgumentList.Add(command);

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex) when (IsStartFailure(ex))
            {
                // Try next shell candidate.
                continue;
            }

            if (proc is null)
            {
                continue;
            }

            using (proc)
            {
                // Start succeeded — do not fall back to another shell if I/O fails
                // (would re-execute the command with side effects).
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
                await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                var combined = string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : stdout + (stdout.Length > 0 && !stdout.EndsWith('\n') ? Environment.NewLine : null) + stderr;
                return (proc.ExitCode, combined.TrimEnd());
            }
        }

        throw new InvalidOperationException(
            OperatingSystem.IsWindows()
                ? "Failed to start a shell (tried pwsh, powershell, cmd)."
                : "Failed to start a shell (tried bash, sh).");
    }

    internal static IEnumerable<(string FileName, string[] Args)> ResolveShellAttempts()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return ("pwsh", ["-NoProfile", "-Command"]);
            yield return ("powershell", ["-NoProfile", "-Command"]);
            yield return ("cmd.exe", ["/c"]);
            yield break;
        }

        yield return ("bash", ["-lc"]);
        yield return ("sh", ["-c"]);
    }

    private static bool IsStartFailure(Exception ex) =>
        ex is Win32Exception
            or FileNotFoundException
            or InvalidOperationException;
}
