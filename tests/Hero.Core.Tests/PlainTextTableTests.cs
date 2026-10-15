using Hero.Core.Plugins;

namespace Hero.Core.Tests;

public sealed class PlainTextTableTests
{
    [Fact]
    public void Format_IncludesHeadersAndCells()
    {
        var text = PlainTextTable.Format(
            ["server", "state", "tools"],
            [
                ["fs", "connected", "3"],
                ["web", "failed", "0"],
            ]);

        Assert.Contains("server", text);
        Assert.Contains("state", text);
        Assert.Contains("tools", text);
        Assert.Contains("fs", text);
        Assert.Contains("connected", text);
        Assert.Contains("web", text);
        Assert.Contains("---", text);
    }

    [Fact]
    public void Format_PadsColumns()
    {
        var text = PlainTextTable.Format(
            ["id", "title"],
            [["1", "hello"]]);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 3);
        Assert.StartsWith("id", lines[0]);
    }
}
