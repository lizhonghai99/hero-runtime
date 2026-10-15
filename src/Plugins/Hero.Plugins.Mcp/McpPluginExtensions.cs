using Hero.Core.Plugins;

namespace Hero.Plugins.Mcp;

public static class McpPluginExtensions
{
    public static AgentBuilder UseMcpPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<McpPlugin>();
    }
}
