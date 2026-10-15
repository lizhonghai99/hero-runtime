using Hero.Core.Session;

namespace Hero.Core.Tests;

public sealed class SessionExportArgsTests
{
    [Fact]
    public void Parse_HtmlFlagAndPath()
    {
        var (html, path) = SessionExportArgs.Parse("/export --html C:\\out\\s.html");
        Assert.True(html);
        Assert.Equal("C:\\out\\s.html", path);
    }

    [Fact]
    public void Parse_PathOnly()
    {
        var (html, path) = SessionExportArgs.Parse("/export ./session.md");
        Assert.False(html);
        Assert.Equal("./session.md", path);
    }
}
