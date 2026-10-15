using System;
using System.Net.Http;
using Hero.Core.Agent;
using Hero.Core.Approval;
using Hero.Core.Harness;
using Hero.Core.Hosting;
using Hero.Core.Observability;
using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Core.Providers;
using Hero.Core.Session;
using Hero.Core.Settings;
using Hero.Core.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hero.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HERO runtime services. Pass <paramref name="configure"/> to add plugins
    /// (e.g. <c>b => b.UseCodingPlugin()</c>). Core stays a minimal socket.
    /// </summary>
    public static IServiceCollection AddHeroAgent(
        this IServiceCollection services,
        Action<AgentBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var agentBuilder = new AgentBuilder();
        configure?.Invoke(agentBuilder);

        services.AddOptions<HeroOptions>()
            .BindConfiguration(HeroOptions.SectionName);

        services.AddOptions<ChatProviderOptions>()
            .BindConfiguration(ChatProviderOptions.SectionName);

        // Pi-style: settings.json + provider env vars.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<ChatProviderOptions>, ChatProviderOptionsConfigurator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HeroOptions>, HeroOptionsConfigurator>());

        foreach (var pluginType in agentBuilder.PluginTypes)
        {
            // Concrete singleton; IPlugin / IHarnessControl factories share that instance.
            // Multiple AddSingleton(IPlugin, …) are aggregated by GetServices / IEnumerable<>.
            services.AddSingleton(pluginType);
            services.AddSingleton(typeof(IPlugin), sp => (IPlugin)sp.GetRequiredService(pluginType));

            if (typeof(IHarnessControl).IsAssignableFrom(pluginType))
            {
                services.AddSingleton(
                    typeof(IHarnessControl),
                    sp => (IHarnessControl)sp.GetRequiredService(pluginType));
            }
        }

        // SessionRunOptions is registered by the host (CLI) with --continue flags.
        services.TryAddSingleton<IMafSessionStore, JsonlMafSessionStore>();
        services.TryAddSingleton(sp =>
            ChatTraceStore.Create(sp.GetRequiredService<IOptions<HeroOptions>>()));
        services.TryAddSingleton<PluginCatalog>();
        services.TryAddSingleton<SkillsHost>();
        services.TryAddSingleton<ITurnLogger, JsonlTurnLogger>();
        services.TryAddSingleton<IToolApprovalHandler, RejectingToolApprovalHandler>();
        services.TryAddSingleton<IAgentRuntime, MafAgentRuntime>();
        services.TryAddSingleton<SettingsStore>();
        services.TryAddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.TryAddSingleton<OpenAiCompatibleModelCatalog>();
        services.TryAddSingleton<AgentHost>();

        return services;
    }
}
