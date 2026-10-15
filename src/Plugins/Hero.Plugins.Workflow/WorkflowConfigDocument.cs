namespace Hero.Plugins.Workflow;

/// <summary>Shape of project/user <c>workflow.json</c>.</summary>
public sealed class WorkflowConfigDocument
{
    /// <summary>When false or missing with no file, the plugin stays silent (no tool).</summary>
    public bool? Enabled { get; set; }

    /// <summary>Persist checkpoints under <c>.hero-agent/workflow-checkpoints/</c>.</summary>
    public bool? Checkpoint { get; set; }

    /// <summary>Per-run timeout in seconds (0 = no timeout).</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>Skip console HITL and auto-approve plans.</summary>
    public bool? AutoApprove { get; set; }
}
