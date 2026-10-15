using Hero.Cli;
using Xunit;

namespace Hero.Core.Tests;

public sealed class CliArgsJsonTests
{
    [Fact]
    public void Parse_JsonAfterPrint_StripsFlagFromPrompt()
    {
        var args = CliArgs.Parse(["-p", "hello world", "--json"]);
        Assert.True(args.Json);
        Assert.Equal("hello world", args.PrintPrompt);
    }

    [Fact]
    public void Parse_JsonBeforePrint_Works()
    {
        var args = CliArgs.Parse(["--json", "-p", "hi"]);
        Assert.True(args.Json);
        Assert.Equal("hi", args.PrintPrompt);
    }

    [Fact]
    public void Parse_WithoutJson_Unchanged()
    {
        var args = CliArgs.Parse(["-p", "only"]);
        Assert.False(args.Json);
        Assert.Equal("only", args.PrintPrompt);
    }
}
