using Hero.Core.Approval;

namespace Hero.Core.Tests;

public sealed class ToolApprovalModeParserTests
{
    [Theory]
    [InlineData(null, ToolApprovalMode.Off)]
    [InlineData("", ToolApprovalMode.Off)]
    [InlineData("off", ToolApprovalMode.Off)]
    [InlineData("none", ToolApprovalMode.Off)]
    [InlineData("shell", ToolApprovalMode.Shell)]
    [InlineData("SH", ToolApprovalMode.Shell)]
    [InlineData("all", ToolApprovalMode.All)]
    [InlineData("on", ToolApprovalMode.All)]
    [InlineData("auto", ToolApprovalMode.Auto)]
    [InlineData("yes", ToolApprovalMode.Auto)]
    public void Parse_KnownValues(string? input, ToolApprovalMode expected)
    {
        Assert.Equal(expected, ToolApprovalModeParser.Parse(input));
    }

    [Fact]
    public void Parse_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => ToolApprovalModeParser.Parse("maybe"));
    }

    [Theory]
    [InlineData(ToolApprovalMode.Off, "off")]
    [InlineData(ToolApprovalMode.Shell, "shell")]
    [InlineData(ToolApprovalMode.All, "all")]
    [InlineData(ToolApprovalMode.Auto, "auto")]
    public void ToSettingsValue_RoundTrips(ToolApprovalMode mode, string expected)
    {
        Assert.Equal(expected, ToolApprovalModeParser.ToSettingsValue(mode));
        Assert.Equal(mode, ToolApprovalModeParser.Parse(expected));
    }

    [Fact]
    public void HelperFlags_MatchModes()
    {
        Assert.True(ToolApprovalModeParser.RequiresShellApproval(ToolApprovalMode.Shell));
        Assert.True(ToolApprovalModeParser.RequiresShellApproval(ToolApprovalMode.Auto));
        Assert.True(ToolApprovalModeParser.RequiresAllToolsApproval(ToolApprovalMode.All));
        Assert.True(ToolApprovalModeParser.RequiresAllToolsApproval(ToolApprovalMode.Auto));
        Assert.True(ToolApprovalModeParser.UsesAutoHandler(ToolApprovalMode.Auto));
        Assert.False(ToolApprovalModeParser.UsesAutoHandler(ToolApprovalMode.Shell));
    }
}
