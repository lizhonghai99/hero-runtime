using System;
using Hero.Core.Approval;
using Hero.Core.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hero.Plugins.Workflow;

/// <summary>
/// MAF Workflows plugin: predefined multi-step research pipeline with HITL + optional checkpoints.
/// Does not replace the interactive Harness agent loop.
/// Silent (no tool/instructions) when <c>workflow.json</c> is missing or <c>enabled</c> is not true.
/// </summary>
public sealed class WorkflowPlugin : IPlugin
{
    public const string PluginId = "workflow";

    private readonly ILoggerFactory _loggerFactory;
    private WorkflowSessionHost? _host;

    public WorkflowPlugin(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public string Id => PluginId;

    public void Configure(IPluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var logger = _loggerFactory.CreateLogger("Hero.Plugins.Workflow");
        _host = new WorkflowSessionHost(builder.Options.WorkingDirectory, logger);

        // Align with CLI --yes / toolApproval=auto: skip workflow HITL prompts.
        if (ToolApprovalModeParser.UsesAutoHandler(ToolApprovalModeParser.Parse(builder.Options.ToolApproval)))
        {
            _host.ApprovalHandler = static (_, _) => Task.FromResult(true);
        }

        builder
            .SetMetadata("workflow", _host.IsEnabled ? "enabled" : "idle")
            .AddDisposable(_host)
            .AddSlashCommand(new WorkflowSlashCommand(_host))
            .AddChatClientDecorator(client => _host.AttachChatClient(client));

        if (!_host.IsEnabled)
        {
            logger.LogDebug("Workflow plugin idle: workflow.json not enabled");
            return;
        }

        builder
            .AddTool(_host.CreateRunWorkflowTool())
            .AppendInstructions(() => _host.BuildInstructionAppendix());
    }
}
