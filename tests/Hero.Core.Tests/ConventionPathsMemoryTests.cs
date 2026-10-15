using Hero.Core.Convention;
using Xunit;

namespace Hero.Core.Tests;

public sealed class ConventionPathsMemoryTests
{
    [Fact]
    public void UserMemoryRoot_IsSiblingOfSessionsStylePath()
    {
        var cwd = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hero-mem-" + Guid.NewGuid().ToString("N")));
        var root = ConventionPaths.UserMemoryRoot(cwd);

        Assert.Contains(Path.DirectorySeparatorChar + "memory" + Path.DirectorySeparatorChar, root);
        Assert.StartsWith(ConventionPaths.UserRoot, root);
        Assert.DoesNotContain("sessions", root, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("logs", root, StringComparison.OrdinalIgnoreCase);
    }
}
