using Hero.Core.Tools;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class ReadFileToolTests
{
    [Fact]
    public async Task ReadFile_ReturnsContent()
    {
        using var temp = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "note.txt"), "hello-hero");

        var fn = ReadFileTool.Create(temp.Path);
        var result = await InvokeAsync(fn, new Dictionary<string, object?> { ["path"] = "note.txt" });

        Assert.Equal("hello-hero", result);
    }

    [Fact]
    public async Task ReadFile_PathEscape_Throws()
    {
        using var temp = new TempDir();
        var fn = ReadFileTool.Create(temp.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(fn, new Dictionary<string, object?> { ["path"] = ".." + Path.DirectorySeparatorChar + "outside.txt" }));
    }

    [Fact]
    public async Task ReadFile_Missing_ThrowsFileNotFound()
    {
        using var temp = new TempDir();
        var fn = ReadFileTool.Create(temp.Path);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            InvokeAsync(fn, new Dictionary<string, object?> { ["path"] = "missing.txt" }));
    }

    [Fact]
    public async Task ReadFile_TooLarge_Throws()
    {
        using var temp = new TempDir();
        var big = Path.Combine(temp.Path, "big.bin");
        await using (var stream = File.Create(big))
        {
            stream.SetLength((256 * 1024) + 1);
        }

        var fn = ReadFileTool.Create(temp.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(fn, new Dictionary<string, object?> { ["path"] = "big.bin" }));
    }

    private static async Task<string> InvokeAsync(AIFunction fn, Dictionary<string, object?> args)
    {
        var raw = await fn.InvokeAsync(new AIFunctionArguments(args));
        return AiFunctionResult.AsString(raw);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "hero-read-" + Guid.NewGuid().ToString("N"));

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
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
