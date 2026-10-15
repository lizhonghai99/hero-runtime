using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hero.Core.Plugins;

internal sealed class PluginBuilder : IPluginBuilder
{
    private readonly List<AITool> _tools = [];
    private readonly List<AIContextProvider> _providers = [];
    private readonly List<SubAgentRegistration> _subAgents = [];
    private readonly List<Func<string>> _instructions = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<Func<CancellationToken, Task>> _warmups = [];
    private readonly List<Func<CancellationToken, Task<IEnumerable<AITool>>>> _deferredToolFactories = [];
    private readonly List<string> _skillsRoots = [];
    private readonly List<Func<IChatClient, IChatClient>> _chatClientDecorators = [];
    private readonly List<ISlashCommand> _slashCommands = [];

    public PluginBuilder(HeroOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public HeroOptions Options { get; }

    public IReadOnlyList<AITool> Tools => _tools;

    public IReadOnlyList<AIContextProvider> ContextProviders => _providers;

    public IReadOnlyList<SubAgentRegistration> SubAgents => _subAgents;

    public IReadOnlyList<Func<string>> InstructionFactories => _instructions;

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public IReadOnlyList<IAsyncDisposable> Disposables => _disposables;

    public IReadOnlyList<Func<CancellationToken, Task>> Warmups => _warmups;

    public IReadOnlyList<Func<CancellationToken, Task<IEnumerable<AITool>>>> DeferredToolFactories =>
        _deferredToolFactories;

    public IReadOnlyList<string> SkillsRoots => _skillsRoots;

    public IReadOnlyList<Func<IChatClient, IChatClient>> ChatClientDecorators => _chatClientDecorators;

    public IReadOnlyList<ISlashCommand> SlashCommands => _slashCommands;

    public IPluginBuilder AddTool(AITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools.Add(tool);
        return this;
    }

    public IPluginBuilder AddDeferredTools(Func<CancellationToken, Task<IEnumerable<AITool>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _deferredToolFactories.Add(factory);
        return this;
    }

    public IPluginBuilder AddContextProvider(AIContextProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        return this;
    }

    public IPluginBuilder AddSubAgent(string name, string description, Func<IChatClient, AIAgent> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(factory);

        _subAgents.Add(new SubAgentRegistration
        {
            Name = name,
            Description = description,
            Factory = factory,
        });
        return this;
    }

    public IPluginBuilder AppendInstructions(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var snapshot = text.Trim();
            _instructions.Add(() => snapshot);
        }

        return this;
    }

    public IPluginBuilder AppendInstructions(Func<string> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _instructions.Add(factory);
        return this;
    }

    public IPluginBuilder SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _metadata[key] = value ?? string.Empty;
        return this;
    }

    public IPluginBuilder AddDisposable(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _disposables.Add(resource);
        return this;
    }

    public IPluginBuilder AddWarmup(Func<CancellationToken, Task> warmup)
    {
        ArgumentNullException.ThrowIfNull(warmup);
        _warmups.Add(warmup);
        return this;
    }

    public IPluginBuilder AddSkillsRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _skillsRoots.Add(path);
        return this;
    }

    public IPluginBuilder AddChatClientDecorator(Func<IChatClient, IChatClient> decorate)
    {
        ArgumentNullException.ThrowIfNull(decorate);
        _chatClientDecorators.Add(decorate);
        return this;
    }

    public IPluginBuilder AddSlashCommand(ISlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        _slashCommands.Add(command);
        return this;
    }
}
