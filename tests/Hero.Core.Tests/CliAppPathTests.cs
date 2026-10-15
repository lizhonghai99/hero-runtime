using Hero.Cli;
using Xunit;

namespace Hero.Core.Tests;

public sealed class CliAppPathTests
{
    [Theory]
    [InlineData(@"D:\LearnCode\hero-runtime\src\Host\Hero.Cli\bin\Debug\net10.0", true)]
    [InlineData(@"D:\LearnCode\hero-runtime\src\Host\Hero.Cli\bin\Release\net10.0", true)]
    [InlineData(@"D:\LearnCode\hero-runtime", false)]
    [InlineData(@"D:\LearnCode\hero-runtime\src\Host\Hero.Cli", false)]
    public void LooksLikeBuildOutputDirectory_DetectsBinOutput(string path, bool expected)
    {
        Assert.Equal(expected, CliApp.LooksLikeBuildOutputDirectory(path));
    }
}
