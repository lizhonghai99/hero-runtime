using Hero.Core.Session;

namespace Hero.Core.Tests;

public sealed class SessionExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesJsonAndMarkdown()
    {
        var root = Directory.CreateTempSubdirectory("hero-export-").FullName;
        try
        {
            var tree = new SessionTreeSnapshot
            {
                WorkingDirectory = root,
                FilePath = Path.Combine(root, "current.jsonl"),
                TipId = "n2",
                Nodes =
                [
                    new SessionNode
                    {
                        Id = "n1",
                        ParentId = null,
                        UserPreview = "hello",
                        AssistantPreview = "hi there",
                        At = DateTimeOffset.UtcNow,
                    },
                    new SessionNode
                    {
                        Id = "n2",
                        ParentId = "n1",
                        UserPreview = "second",
                        AssistantPreview = "ok",
                        At = DateTimeOffset.UtcNow,
                    },
                ],
            };

            var outDir = Path.Combine(root, "out");
            var result = await SessionExporter.ExportAsync(tree, outDir);

            Assert.True(File.Exists(result.JsonPath));
            Assert.True(File.Exists(result.MarkdownPath));

            var json = await File.ReadAllTextAsync(result.JsonPath);
            Assert.Contains("n2", json, StringComparison.Ordinal);
            Assert.Contains("hello", json, StringComparison.Ordinal);

            var md = await File.ReadAllTextAsync(result.MarkdownPath);
            Assert.Contains("# HERO session export", md, StringComparison.Ordinal);
            Assert.Contains("`n2`", md, StringComparison.Ordinal);
            Assert.Contains("second", md, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildMarkdown_MarksTip()
    {
        var tree = new SessionTreeSnapshot
        {
            WorkingDirectory = "/tmp/demo",
            FilePath = "/tmp/demo/current.jsonl",
            TipId = "tip",
            Nodes =
            [
                new SessionNode
                {
                    Id = "tip",
                    ParentId = null,
                    UserPreview = "ask",
                    At = DateTimeOffset.UnixEpoch,
                },
            ],
        };

        var md = SessionExporter.BuildMarkdown(tree);
        Assert.Contains("* `tip`", md, StringComparison.Ordinal);
    }
}
