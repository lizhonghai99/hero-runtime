using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;

namespace Hero.Plugins.Workflow;

internal sealed class WorkflowSlashCommand : ISlashCommand
{
    private readonly WorkflowSessionHost _host;

    public WorkflowSlashCommand(WorkflowSessionHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public string Name => "workflow";

    public string Description => "Run the research workflow: /workflow <task> (or /workflow for status)";

    public async Task<bool> TryHandleAsync(
        string line,
        ISlashCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!line.StartsWith("/workflow", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var arg = line.Length > "/workflow".Length
            ? line["/workflow".Length..].Trim()
            : string.Empty;

        _host.ReloadConfig();

        if (string.IsNullOrWhiteSpace(arg))
        {
            context.WriteLine(_host.DescribeStatus());
            return true;
        }

        if (!_host.IsEnabled)
        {
            context.WriteError(_host.DescribeStatus());
            return true;
        }

        context.WriteLine($"workflow> starting: {arg}");
        var result = await _host.RunAsync(arg, cancellationToken).ConfigureAwait(false);

        if (result.Cancelled)
        {
            context.WriteError("workflow> cancelled or timed out");
            return true;
        }

        if (!result.Succeeded)
        {
            context.WriteError($"workflow> failed: {result.Status}");
            return true;
        }

        context.WriteLine("workflow> " + (result.Output ?? "(empty)"));
        return true;
    }
}
