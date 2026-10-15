using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Approval;

namespace Hero.Cli.Tui;

/// <summary>Tool approval via Terminal.Gui <c>MessageBox</c> (four-way choice).</summary>
internal sealed class TuiToolApprovalHandler : IToolApprovalHandler
{
    private readonly TuiDialogService _dialogs;

    public TuiToolApprovalHandler(TuiDialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public async Task<ToolApprovalDecision> RequestApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var message =
            $"{request.ToolName}({request.ArgumentsPreview})\n\n" +
            "[1] once   [2] always tool   [3] always args   [4] reject";

        int index;
        try
        {
            // Last button is default (Enter) → Reject last so accidental Enter is safe-ish;
            // order: Once, AlwaysTool, AlwaysArgs, Reject — default Reject.
            index = await _dialogs
                .QueryAsync(
                    "Tool approval",
                    message,
                    cancellationToken,
                    "Once",
                    "Always tool",
                    "Always args",
                    "Reject")
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ToolApprovalDecision.Reject;
        }

        return index switch
        {
            0 => ToolApprovalDecision.ApproveOnce,
            1 => ToolApprovalDecision.ApproveAlwaysTool,
            2 => ToolApprovalDecision.ApproveAlwaysArgs,
            _ => ToolApprovalDecision.Reject,
        };
    }
}
