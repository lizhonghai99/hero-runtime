using System;
using System.ComponentModel;
using System.IO;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tools;

/// <summary>Shared primitive for plugins: read a UTF-8 file under the working directory.</summary>
public static class ReadFileTool
{
    public static AIFunction Create(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var root = Path.GetFullPath(workingDirectory);

        return AIFunctionFactory.Create(
            ReadAsync,
            name: "read_file",
            description: "Read a UTF-8 text file under the agent working directory. Path is relative to that directory.");

        string ReadAsync(
            [Description("Relative path under the working directory, e.g. README.md or src/Program.cs")] string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("path is required.", nameof(path));
            }

            var full = PathGuard.ResolveUnderRoot(root, path);

            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"File not found: {path}", full);
            }

            var info = new FileInfo(full);
            const long maxBytes = 256 * 1024;
            if (info.Length > maxBytes)
            {
                throw new InvalidOperationException($"File too large ({info.Length} bytes). Max {maxBytes}.");
            }

            return File.ReadAllText(full);
        }
    }
}
