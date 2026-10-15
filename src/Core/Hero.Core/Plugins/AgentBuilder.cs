using System;
using System.Collections.Generic;

namespace Hero.Core.Plugins;

/// <summary>
/// Host-side registration of plugins when calling <c>AddHeroAgent</c>.
/// </summary>
public sealed class AgentBuilder
{
    private readonly List<Type> _pluginTypes = [];

    public IReadOnlyList<Type> PluginTypes => _pluginTypes;

    public AgentBuilder AddPlugin<TPlugin>()
        where TPlugin : class, IPlugin
    {
        _pluginTypes.Add(typeof(TPlugin));
        return this;
    }

    public AgentBuilder AddPlugin(Type pluginType)
    {
        ArgumentNullException.ThrowIfNull(pluginType);
        if (!typeof(IPlugin).IsAssignableFrom(pluginType))
        {
            throw new ArgumentException($"Type {pluginType.Name} does not implement IPlugin.", nameof(pluginType));
        }

        _pluginTypes.Add(pluginType);
        return this;
    }
}
