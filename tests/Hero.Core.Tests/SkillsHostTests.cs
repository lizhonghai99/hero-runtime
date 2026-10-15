using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Core.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hero.Core.Tests;

public sealed class SkillsHostTests
{
    [Fact]
    public void PluginSkillsRoot_CountsSkillMarkdownOneLevelDeep()
    {
        using var temp = new TempDir();
        var pluginSkills = Path.Combine(temp.Path, "skills", "coding");
        var skillDir = Path.Combine(pluginSkills, "shell-safety");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: shell-safety\n---\n# shell-safety\n");

        // Project/user roots: empty cwd isolate so only plugin root counts.
        var cwd = Path.Combine(temp.Path, "cwd");
        Directory.CreateDirectory(cwd);

        var options = Microsoft.Extensions.Options.Options.Create(new HeroOptions { WorkingDirectory = cwd });
        var catalog = new PluginCatalog(
            [new SkillsRootPlugin(pluginSkills)],
            options,
            NullLogger<PluginCatalog>.Instance);

        var host = new SkillsHost(options, catalog, NullLogger<SkillsHost>.Instance);

        Assert.Equal(1, host.PluginSkillCount);
        Assert.Equal(0, host.ProjectSkillCount);
        Assert.Contains(pluginSkills, host.Roots);
        Assert.True(host.HasSkills);
        Assert.NotNull(host.CreateSkillsSource());
    }

    [Fact]
    public void ListSkillNames_ReturnsFolderNamesOnly()
    {
        using var temp = new TempDir();
        var cwd = Path.Combine(temp.Path, "cwd");
        var skillA = Path.Combine(cwd, ".hero-agent", "skills", "alpha");
        var skillB = Path.Combine(cwd, ".hero-agent", "skills", "beta");
        Directory.CreateDirectory(skillA);
        Directory.CreateDirectory(skillB);
        File.WriteAllText(Path.Combine(skillA, "SKILL.md"), "# alpha\n");
        File.WriteAllText(Path.Combine(skillB, "SKILL.md"), "# beta\n");
        File.WriteAllText(Path.Combine(skillA, "extra.md"), "not a skill body marker");

        var options = Microsoft.Extensions.Options.Options.Create(new HeroOptions { WorkingDirectory = cwd });
        var catalog = new PluginCatalog([], options, NullLogger<PluginCatalog>.Instance);
        var host = new SkillsHost(options, catalog, NullLogger<SkillsHost>.Instance);

        var names = host.ListSkillNames();
        Assert.Equal(["alpha", "beta"], names);
    }

    [Fact]
    public void PluginSkillsRoot_MissingDirectory_CountsZero()
    {
        using var temp = new TempDir();
        var cwd = Path.Combine(temp.Path, "cwd");
        Directory.CreateDirectory(cwd);
        var missing = Path.Combine(temp.Path, "skills", "coding");

        var options = Microsoft.Extensions.Options.Options.Create(new HeroOptions { WorkingDirectory = cwd });
        var catalog = new PluginCatalog(
            [new SkillsRootPlugin(missing)],
            options,
            NullLogger<PluginCatalog>.Instance);

        var host = new SkillsHost(options, catalog, NullLogger<SkillsHost>.Instance);

        Assert.Equal(0, host.PluginSkillCount);
        Assert.DoesNotContain(missing, host.Roots);
        Assert.False(host.HasSkills);
        Assert.Null(host.CreateSkillsSource());
    }

    private sealed class SkillsRootPlugin(string root) : IPlugin
    {
        public string Id => "test-skills";

        public void Configure(IPluginBuilder builder) => builder.AddSkillsRoot(root);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "hero-skills-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
            catch (UnauthorizedAccessException)
            {
                // best-effort cleanup
            }
        }
    }
}
