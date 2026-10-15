using Hero.Core.Session;

namespace Hero.Core.Tests;

public sealed class SessionTreeFormatterTests
{
    [Fact]
    public void FormatLines_MarksTip()
    {
        var tree = new SessionTreeSnapshot
        {
            WorkingDirectory = "D:\\x",
            FilePath = "D:\\x\\s.jsonl",
            TipId = "b",
            Nodes =
            [
                new SessionNode { Id = "a", UserPreview = "hi", At = DateTimeOffset.UnixEpoch },
                new SessionNode { Id = "b", ParentId = "a", UserPreview = "next", At = DateTimeOffset.UnixEpoch.AddMinutes(1) },
            ],
        };

        var lines = SessionTreeFormatter.FormatLines(tree);
        Assert.Contains(lines, l => l.Contains("* b", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("  a", StringComparison.Ordinal) || l.Contains(" a ", StringComparison.Ordinal));
    }
}
