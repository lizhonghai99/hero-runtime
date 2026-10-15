using System;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Cli;

/// <summary>
/// Routes console lines during a turn: priority readers (tool approval / MCP trust)
/// win over the steer pump (guide / follow-up). Avoids dual <see cref="Console.In"/> readers
/// that otherwise swallow approval answers and hang the turn.
/// </summary>
internal sealed class TurnInputBroker
{
    private int _pumpActive;
    private TaskCompletionSource<string?>? _priority;

    public void BeginPump() => Interlocked.Exchange(ref _pumpActive, 1);

    public void EndPump()
    {
        Interlocked.Exchange(ref _pumpActive, 0);
        var pending = Interlocked.Exchange(ref _priority, null);
        pending?.TrySetCanceled();
    }

    /// <summary>If a priority waiter exists, deliver this line to it and return true.</summary>
    public bool TryDeliverPriority(string? line)
    {
        var tcs = Interlocked.Exchange(ref _priority, null);
        if (tcs is null)
        {
            return false;
        }

        return tcs.TrySetResult(line);
    }

    /// <summary>
    /// Read a line for approval / trust. When the turn stdin pump is active, wait for the
    /// next pumped line; otherwise read the console directly.
    /// </summary>
    /// <remarks>
    /// Idle path still uses <see cref="Console.In"/> (sync-on-Windows). Callers must not
    /// hold a concurrent steer pump — during turns, prefer the pump + <see cref="TryDeliverPriority"/>.
    /// </remarks>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _pumpActive) == 0)
        {
            // SyncTextReader.ReadLineAsync blocks the thread on Windows; OK only when no
            // concurrent Console.Out writer is in flight (warmup / idle prompts).
            return await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.CompareExchange(ref _priority, tcs, null) is null)
            {
                break;
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            using var reg = cancellationToken.Register(static state =>
            {
                ((TaskCompletionSource<string?>)state!).TrySetCanceled();
            }, tcs);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            Interlocked.CompareExchange(ref _priority, null, tcs);
        }
    }
}
