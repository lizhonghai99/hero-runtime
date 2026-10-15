using Hero.Core.Plugins;

namespace Hero.Plugins.Coding;

public static class CodingPluginExtensions
{
    public static AgentBuilder UseCodingPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<CodingPlugin>();
    }
}
