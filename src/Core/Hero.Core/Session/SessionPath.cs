using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hero.Core.Session;

internal static class SessionPath
{
    public static string NormalizeWorkingDirectory(string workingDirectory)
    {
        return Path.GetFullPath(string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : workingDirectory);
    }

    public static string GetDirectoryKey(string workingDirectory)
    {
        var full = NormalizeWorkingDirectory(workingDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12].ToLowerInvariant();
        var leaf = MakeSafeLeaf(full);
        return $"{leaf}-{hash}";
    }

    private static string MakeSafeLeaf(string fullPath)
    {
        var leaf = fullPath
            .Replace(':', '-')
            .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-')
            .Trim('-');

        if (leaf.Length > 48)
        {
            leaf = leaf[^48..];
        }

        return string.IsNullOrWhiteSpace(leaf) ? "cwd" : leaf;
    }
}
