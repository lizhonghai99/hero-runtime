using Hero.Core.Hosting;
using Xunit;

namespace Hero.Core.Tests;

public sealed class BuiltinSlashHelpTests
{
    [Fact]
    public void HelpLines_IncludeCompactMemorySkills()
    {
        var help = BuiltinSlashCommands.HelpLines(null);
        Assert.Contains(help, line => line.StartsWith("/compact", StringComparison.Ordinal));
        Assert.Contains(help, line => line.StartsWith("/memory", StringComparison.Ordinal));
        Assert.Contains(help, line => line.StartsWith("/skills", StringComparison.Ordinal));
    }
}
