namespace Hero.Core.Plugins;

/// <summary>
/// Extension surface for Hero. Core stays a harness socket; features arrive as plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>Stable id, e.g. <c>coding</c>.</summary>
    string Id { get; }

    void Configure(IPluginBuilder builder);
}
