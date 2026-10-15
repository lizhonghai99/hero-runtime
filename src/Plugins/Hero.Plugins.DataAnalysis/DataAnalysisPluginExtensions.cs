using Hero.Core.Plugins;

namespace Hero.Plugins.DataAnalysis;

public static class DataAnalysisPluginExtensions
{
    public static AgentBuilder UseDataAnalysisPlugin(this AgentBuilder builder)
    {
        return builder.AddPlugin<DataAnalysisPlugin>();
    }
}
