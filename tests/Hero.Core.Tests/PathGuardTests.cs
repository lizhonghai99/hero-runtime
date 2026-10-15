using Hero.Core.Tools;
using Xunit;

namespace Hero.Core.Tests;

public sealed class PathGuardTests
{
    [Fact]
    public void ResolveUnderRoot_RejectsAbsoluteEscape()
    {
        using var temp = new TempDir();
        var outside = Path.Combine(Path.GetTempPath(), "hero-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "secret");
        try
        {
            Assert.Throws<InvalidOperationException>(() => PathGuard.ResolveUnderRoot(temp.Path, outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void ResolveUnderRoot_RejectsDotDot()
    {
        using var temp = new TempDir();
        Assert.Throws<InvalidOperationException>(() =>
            PathGuard.ResolveUnderRoot(temp.Path, ".." + Path.DirectorySeparatorChar + "outside.txt"));
    }

    [Fact]
    public void ResolveUnderRoot_RejectsSymlinkEscape()
    {
        using var temp = new TempDir();
        var outside = Path.Combine(Path.GetTempPath(), "hero-link-target-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(outside, "secret");
        var link = Path.Combine(temp.Path, "escape.txt");
        try
        {
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Symlinks often need admin / Developer Mode on Windows.
                return;
            }

            Assert.Throws<InvalidOperationException>(() => PathGuard.ResolveUnderRoot(temp.Path, "escape.txt"));
        }
        finally
        {
            try
            {
                if (File.Exists(link))
                {
                    File.Delete(link);
                }
            }
            catch (IOException)
            {
            }

            try
            {
                File.Delete(outside);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "hero-pathguard-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
