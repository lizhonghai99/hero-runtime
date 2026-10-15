using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Workflow.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Hero.Plugins.Workflow.Executors;

/// <summary>
/// Research step: gates on HITL approval, then runs an <see cref="AIAgent"/> (agent-as-node).
/// Tests may inject <see cref="Func{String, CancellationToken, Task{String}}"/> instead of a live model.
/// </summary>
internal sealed class ResearchExecutor : Executor<PlanDecision, string>
{
    private readonly AIAgent? _agent;
    private readonly Func<string, CancellationToken, Task<string>>? _stub;

    public ResearchExecutor(AIAgent agent)
        : base("Research")
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    }

    public ResearchExecutor(Func<string, CancellationToken, Task<string>> stub)
        : base("Research")
    {
        _stub = stub ?? throw new ArgumentNullException(nameof(stub));
    }

    public override async ValueTask<string> HandleAsync(
        PlanDecision message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (!message.Approved)
        {
            var note = string.IsNullOrWhiteSpace(message.Note) ? "user declined" : message.Note.Trim();
            return $"REJECTED: {note}";
        }

        var prompt =
            $"""
            Task: {message.Task}

            Approved plan:
            {message.Plan}

            Produce concise research notes (facts only, no fluff).
            """;

        if (_stub is not null)
        {
            return await _stub(prompt, cancellationToken).ConfigureAwait(false);
        }

        var response = await _agent!.RunAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ExtractText(response);
    }

    internal static string ExtractText(AgentResponse response)
    {
        if (response is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return response.Text.Trim();
        }

        var parts = response.Messages?
            .SelectMany(m => m.Contents ?? [])
            .OfType<TextContent>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return parts is null ? string.Empty : string.Join("\n", parts).Trim();
    }
}
