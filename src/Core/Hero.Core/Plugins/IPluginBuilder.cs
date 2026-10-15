using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Plugins;

/// <summary>
/// What a plugin may contribute when the host builds an agent.
/// </summary>
public interface IPluginBuilder
{
    HeroOptions Options { get; }

    IPluginBuilder AddTool(AITool tool);

    /// <summary>
    /// Register tools discovered asynchronously during <see cref="PluginCatalog.WarmupAsync"/>.
    /// Resolved before the agent is built (see <c>MafAgentRuntime</c>).
    /// </summary>
    IPluginBuilder AddDeferredTools(Func<CancellationToken, Task<IEnumerable<AITool>>> factory);

    IPluginBuilder AddContextProvider(AIContextProvider provider);

    /// <summary>
    /// Registers a background/sub agent factory. Invoked once when the chat client is ready.
    /// </summary>
    IPluginBuilder AddSubAgent(
        string name,
        string description,
        Func<IChatClient, AIAgent> factory);

    /// <summary>Appended to the main agent system instructions.</summary>
    IPluginBuilder AppendInstructions(string text);

    /// <summary>
    /// Appended at agent-build time (after warmup), so plugins can include discovered tools.
    /// </summary>
    IPluginBuilder AppendInstructions(Func<string> factory);

    /// <summary>Host-visible metadata (e.g. shell selection for the CLI banner).</summary>
    IPluginBuilder SetMetadata(string key, string value);

    /// <summary>Owned resource disposed with the plugin catalog.</summary>
    IPluginBuilder AddDisposable(IAsyncDisposable resource);

    /// <summary>Optional warmup before the first turn (e.g. shell env probe).</summary>
    IPluginBuilder AddWarmup(Func<CancellationToken, Task> warmup);

    /// <summary>
    /// Extra Agent Skills root (directory of <c>*/SKILL.md</c>). Merged after project skills, before user skills.
    /// </summary>
    IPluginBuilder AddSkillsRoot(string path);

    /// <summary>
    /// Wrap the shared <see cref="IChatClient"/> after it is created (e.g. tracing middleware).
    /// Decorators apply in registration order (outermost last).
    /// </summary>
    IPluginBuilder AddChatClientDecorator(Func<IChatClient, IChatClient> decorate);

    /// <summary>REPL slash command contributed by this plugin (e.g. <c>/trace</c>).</summary>
    IPluginBuilder AddSlashCommand(ISlashCommand command);
}
