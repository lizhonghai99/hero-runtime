using System;
using System.Threading;

namespace Hero.Cli;

/// <summary>
/// Ctrl+C: cancel the in-flight turn first; at idle, signal app exit (unless TUI owns Ctrl+C).
/// Does not kill the process on the first Ctrl+C during streaming.
/// </summary>
internal sealed class ConsoleCancelScope : IDisposable
{
    private readonly CancellationTokenSource _appCts = new();
    private CancellationTokenSource? _turnCts;
    private int _disposed;

    public ConsoleCancelScope()
    {
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    public CancellationToken AppToken => _appCts.Token;

    /// <summary>
    /// When true, Console Ctrl+C never exits the process (TUI handles clear/quit itself).
    /// Still cancels an in-flight turn.
    /// </summary>
    public bool TuiOwnsCtrlC { get; set; }

    public CancellationTokenSource BeginTurn()
    {
        _turnCts?.Dispose();
        _turnCts = CancellationTokenSource.CreateLinkedTokenSource(_appCts.Token);
        return _turnCts;
    }

    public void EndTurn()
    {
        _turnCts?.Dispose();
        _turnCts = null;
    }

    /// <summary>Cancel the in-flight turn without signalling app exit.</summary>
    public void CancelTurn() => _turnCts?.Cancel();

    public bool IsTurnActive => _turnCts is { IsCancellationRequested: false };

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Keep process alive so we can finish the REPL cleanly.
        e.Cancel = true;

        if (_turnCts is { IsCancellationRequested: false })
        {
            _turnCts.Cancel();
            return;
        }

        if (TuiOwnsCtrlC)
        {
            return;
        }

        _appCts.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Console.CancelKeyPress -= OnCancelKeyPress;
        EndTurn();
        _appCts.Dispose();
    }
}
