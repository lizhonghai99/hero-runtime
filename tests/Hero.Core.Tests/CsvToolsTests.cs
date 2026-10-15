using Hero.Plugins.DataAnalysis;
using Microsoft.Extensions.AI;

namespace Hero.Core.Tests;

public sealed class CsvToolsTests
{
    [Fact]
    public async Task SummarizeCsv_ReportsMinMaxAvg()
    {
        using var temp = new TempDir();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "sales.csv"),
            """
            name,amount
            a,10
            b,20
            c,30
            """);

        var summarize = GetFunction(temp.Path, "summarize_csv");
        var text = await InvokeAsync(summarize, new Dictionary<string, object?>
        {
            ["path"] = "sales.csv",
            ["maxRows"] = 5000,
        });

        Assert.Contains("separator: COMMA", text);
        Assert.Contains("rows_scanned: 3", text);
        Assert.Contains("min=10", text);
        Assert.Contains("max=30", text);
        Assert.Contains("avg=20", text);
    }

    [Fact]
    public async Task SummarizeCsv_TsvUsesTabSeparator()
    {
        using var temp = new TempDir();
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "sales.tsv"),
            "name\tamount\nx\t1\ny\t2\nz\t3\n");

        var summarize = GetFunction(temp.Path, "summarize_csv");
        var text = await InvokeAsync(summarize, new Dictionary<string, object?>
        {
            ["path"] = "sales.tsv",
        });

        Assert.Contains("separator: TAB", text);
        Assert.Contains("rows_scanned: 3", text);
    }

    [Fact]
    public async Task SummarizeCsv_PathEscape_Throws()
    {
        using var temp = new TempDir();
        var summarize = GetFunction(temp.Path, "summarize_csv");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(summarize, new Dictionary<string, object?>
            {
                ["path"] = ".." + Path.DirectorySeparatorChar + "escape.csv",
            }));
    }

    [Fact]
    public async Task SummarizeCsv_EmptyFile_Throws()
    {
        using var temp = new TempDir();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "empty.csv"), string.Empty);
        var summarize = GetFunction(temp.Path, "summarize_csv");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAsync(summarize, new Dictionary<string, object?> { ["path"] = "empty.csv" }));
    }

    [Fact]
    public async Task SummarizeCsv_MissingFile_ThrowsFileNotFound()
    {
        using var temp = new TempDir();
        var summarize = GetFunction(temp.Path, "summarize_csv");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            InvokeAsync(summarize, new Dictionary<string, object?> { ["path"] = "nope.csv" }));
    }

    private static AIFunction GetFunction(string root, string name)
    {
        var tool = CsvTools.CreateAll(root).OfType<AIFunction>().Single(t => t.Name == name);
        return tool;
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
            "hero-csv-" + Guid.NewGuid().ToString("N"));

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
