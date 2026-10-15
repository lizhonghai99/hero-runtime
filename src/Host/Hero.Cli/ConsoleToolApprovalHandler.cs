using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;

namespace Hero.Cli;

/// <summary>
/// Interactive four-way tool approval (once / always-tool / always-args / reject).
/// </summary>
internal sealed class ConsoleToolApprovalHandler : IToolApprovalHandler
{
    private readonly TurnInputBroker _input;

    public ConsoleToolApprovalHandler(TurnInputBroker input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public async Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"approve?> {request.ToolName}({request.ArgumentsPreview})");
        Console.WriteLine("approve?> [1] once  [2] always tool  [3] always args  [4] reject");
        Console.Write("approve?> ");
        Console.ResetColor();

        string? line;
        try
        {
            line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolApprovalDecision.Reject;
        }

        return ToolApprovalConsoleChoice.Parse(line);
    }
}
