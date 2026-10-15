using Hero.Core.Plugins;

namespace Hero.Plugins.Workflow;

public static class WorkflowPluginExtensions
{
    public static AgentBuilder UseWorkflowPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<WorkflowPlugin>();
    }
}
