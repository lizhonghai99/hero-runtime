using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Workflow.Models;

namespace Hero.Plugins.Workflow;

/// <summary>HITL callback for the workflow approve port (y/N style).</summary>
public delegate Task<bool> WorkflowApprovalHandler(PlanDraft draft, CancellationToken cancellationToken);

/// <summary>Console HITL matching tool-approval tone.</summary>
public static class WorkflowConsoleApproval
{
    public static async Task<bool> PromptAsync(PlanDraft draft, CancellationToken cancellationToken = default)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"workflow approve?> task: {draft.Task}");
        Console.WriteLine("workflow approve?> plan:");
        Console.ResetColor();
        Console.WriteLine(draft.Plan);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("workflow approve?> [y/N] ");
        Console.ResetColor();

        string? line;
        try
        {
            line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        return trimmed.Equals("y", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
