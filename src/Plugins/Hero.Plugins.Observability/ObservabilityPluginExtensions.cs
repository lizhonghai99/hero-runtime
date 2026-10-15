using Hero.Core.Plugins;

namespace Hero.Plugins.Observability;

public static class ObservabilityPluginExtensions
{
    public static AgentBuilder UseObservabilityPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<ObservabilityPlugin>();
    }
}
