using System;
using System.IO;

namespace Hero.Core.Tools;

/// <summary>
/// Resolve and validate paths so they stay under a working-directory root
/// (lexical + final symlink target).
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Resolve <paramref name="path"/> under <paramref name="rootDirectory"/>.
    /// Absolute paths and <c>..</c> that escape the root are rejected.
    /// Symlink final targets outside the root are rejected.
    /// </summary>
    public static string ResolveUnderRoot(string rootDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("path is required.", nameof(path));
        }

        var root = Path.GetFullPath(rootDirectory);
        var full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));

        EnsureUnderRoot(root, full, path);

        if (File.Exists(full) || Directory.Exists(full))
        {
            try
            {
                var link = File.ResolveLinkTarget(full, returnFinalTarget: true)
                    ?? Directory.ResolveLinkTarget(full, returnFinalTarget: true);
                if (link is not null)
                {
                    var real = Path.GetFullPath(link.FullName);
                    EnsureUnderRoot(root, real, path);
                }
            }
            catch (IOException)
            {
                // Unreadable reparse point — fall through; subsequent open may fail.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return full;
    }

    public static bool IsUnderRoot(string rootDirectory, string fullPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var full = Path.GetFullPath(fullPath);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureUnderRoot(string root, string full, string original)
    {
        if (!IsUnderRoot(root, full))
        {
            throw new InvalidOperationException($"Path escapes working directory: {original}");
        }
    }
}
