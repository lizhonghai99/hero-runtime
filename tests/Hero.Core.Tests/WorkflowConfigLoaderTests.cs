using Hero.Plugins.Workflow;

namespace Hero.Core.Tests;

public sealed class WorkflowConfigLoaderTests
{
    [Fact]
    public void IsEnabled_RequiresExplicitTrue()
    {
        Assert.False(WorkflowConfigLoader.IsEnabled(new WorkflowConfigDocument()));
        Assert.False(WorkflowConfigLoader.IsEnabled(new WorkflowConfigDocument { Enabled = false }));
        Assert.True(WorkflowConfigLoader.IsEnabled(new WorkflowConfigDocument { Enabled = true }));
    }

    [Fact]
    public void LoadMerged_ProjectWinsOverUser()
    {
        var root = Path.Combine(Path.GetTempPath(), "hero-wf-cfg-" + Guid.NewGuid().ToString("N"));
        var projectHero = Path.Combine(root, ".hero-agent");
        Directory.CreateDirectory(projectHero);
        var userPath = Path.Combine(root, "user-workflow.json");

        File.WriteAllText(userPath, """{ "enabled": true, "timeoutSeconds": 10 }""");
        File.WriteAllText(Path.Combine(projectHero, "workflow.json"), """{ "timeoutSeconds": 30 }""");

        try
        {
            var doc = WorkflowConfigLoader.LoadMerged(root, userPath);
            Assert.True(doc.Enabled);
            Assert.Equal(30, doc.TimeoutSeconds);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
