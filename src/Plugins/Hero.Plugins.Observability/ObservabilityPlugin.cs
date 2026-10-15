using System;
using Hero.Core.Observability;
using Hero.Core.Plugins;

namespace Hero.Plugins.Observability;

/// <summary>
/// CLI presentation for local turn traces: wraps the chat client with <see cref="TracingChatClient"/>
/// (JSONL + <c>/trace</c> / HTML). Harness OpenTelemetry instrumentation stays enabled separately
/// for APM exporters — this plugin does not replace MAF OTel.
/// </summary>
public sealed class ObservabilityPlugin : IPlugin
{
    public const string PluginId = "observability";

    private readonly ChatTraceStore _store;

    public ObservabilityPlugin(ChatTraceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Id => PluginId;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var agentName = string.IsNullOrWhiteSpace(builder.Options.AgentName)
            ? "hi"
            : builder.Options.AgentName.Trim();

        builder
            .AddChatClientDecorator(inner => new TracingChatClient(inner, _store, agentName))
            .AddSlashCommand(new TraceSlashCommand(_store));
    }
}
