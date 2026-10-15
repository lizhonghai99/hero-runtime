using Hero.Cli;
using Xunit;

namespace Hero.Core.Tests;

public sealed class LocalCommandRunnerTests
{
    [Fact]
    public void ResolveShellAttempts_IncludesPosixOnNonWindows()
    {
        var attempts = LocalCommandRunner.ResolveShellAttempts().ToList();
        Assert.NotEmpty(attempts);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(attempts, a => a.FileName is "pwsh" or "powershell" or "cmd.exe");
            Assert.Equal("/c", attempts.First(a => a.FileName == "cmd.exe").Args[0]);
        }
        else
        {
            Assert.Contains(attempts, a => a.FileName == "bash");
            Assert.Contains(attempts, a => a.FileName == "sh");
            Assert.Equal("-lc", attempts.First(a => a.FileName == "bash").Args[0]);
        }
    }
}
