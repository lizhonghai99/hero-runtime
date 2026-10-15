using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;

namespace Hero.Plugins.Harness;

internal sealed class ModeSlashCommand : ISlashCommand
{
    public string Name => "mode";

    public string Description => "/mode [plan|execute] — show or switch harness mode";

    public async Task<bool> TryHandleAsync(
        string line,
        ISlashCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!line.Equals("/mode", StringComparison.OrdinalIgnoreCase)
            && !line.StartsWith("/mode ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                var current = await context.Runtime.GetModeAsync(cancellationToken).ConfigureAwait(false);
                context.WriteLine($"mode> {current}");
                context.WriteLine("usage> /mode plan|execute");
            }
            else
            {
                await context.Runtime.SetModeAsync(parts[1], cancellationToken).ConfigureAwait(false);
                var current = await context.Runtime.GetModeAsync(cancellationToken).ConfigureAwait(false);
                context.WriteLine($"mode> {current}");
            }
        }
        catch (Exception ex)
        {
            context.WriteError($"error> {ex.Message}");
        }

        return true;
    }
}
