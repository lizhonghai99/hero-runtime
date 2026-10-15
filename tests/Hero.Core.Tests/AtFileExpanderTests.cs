using System.IO;
using Hero.Cli;

namespace Hero.Core.Tests;

public sealed class AtFileExpanderTests
{
    [Fact]
    public void Expand_InlinesRelativeFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hi-at-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "note.txt");
            File.WriteAllText(file, "hello-at");
            var result = AtFileExpander.Expand("see @note.txt please", dir);
            Assert.Contains("hello-at", result, StringComparison.Ordinal);
            Assert.Contains("Attached files:", result, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Expand_RejectsParentEscape()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hi-at-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = AtFileExpander.Expand("bad @../secret.txt", dir);
            Assert.Contains("ERROR:", result, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
