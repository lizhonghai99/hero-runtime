using Hero.Core.Plugins;

namespace Hero.Plugins.Harness;

public static class HarnessPluginExtensions
{
    public static AgentBuilder UseHarnessPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<HarnessPlugin>();
    }
}
