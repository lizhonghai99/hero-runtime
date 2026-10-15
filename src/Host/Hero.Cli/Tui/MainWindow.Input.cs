using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Interaction;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Hero.Cli.Tui;

internal partial class MainWindow
{
    private void OnWindowKeyDown(object? sender, Key key)
    {
        // Esc: abort turn + restore queues (busy); ignore when idle.
        if (key == Key.Esc)
        {
            key.Handled = true;
            if (Volatile.Read(ref _turnBusy) != 0)
            {
                _restoreEditorOnAbort = _queues.DrainToEditorText();
                _cancel.CancelTurn();
                AppendSystem("abort — restoring queued input");
            }

            return;
        }

        if (key == Key.C.WithCtrl)
        {
            key.Handled = true;
            var text = GetEditorText();
            if (!string.IsNullOrEmpty(text))
            {
                SetEditorText(string.Empty);
                return;
            }

            App?.RequestStop();
            return;
        }

        if (key == Key.L.WithCtrl)
        {
            key.Handled = true;
            if (Volatile.Read(ref _turnBusy) != 0)
            {
                AppendSystem("busy — wait for the current turn");
                return;
            }

            _ = RunHotkeyAsync(async () =>
            {
                await new TuiModelPicker(_host, _dialogs, msg => Ui(() => AppendSystem(msg)))
                    .RunAsync(_cancel.AppToken)
                    .ConfigureAwait(false);
                await RefreshModeAsync().ConfigureAwait(false);
                Ui(RefreshStatus);
            });
            return;
        }

        if (key == Key.P.WithCtrl)
        {
            key.Handled = true;
            _ = RunHotkeyAsync(() => CycleModelAsync(+1));
            return;
        }

        if (key == Key.P.WithAlt)
        {
            key.Handled = true;
            _ = RunHotkeyAsync(() => CycleModelAsync(-1));
            return;
        }

        if (key == Key.Tab.WithShift)
        {
            key.Handled = true;
            _ = RunHotkeyAsync(async () =>
            {
                var next = await _host.CycleReasoningAsync(_cancel.AppToken).ConfigureAwait(false);
                Ui(() => AppendSystem($"reasoning → {next}"));
                Ui(RefreshStatus);
            });
            return;
        }

        if (key == Key.G.WithCtrl)
        {
            key.Handled = true;
            if (Volatile.Read(ref _turnBusy) != 0)
            {
                AppendSystem("busy — wait for the current turn");
                return;
            }

            _ = RunHotkeyAsync(async () =>
            {
                var edited = await ExternalEditor.EditAsync(GetEditorText(), _cancel.AppToken).ConfigureAwait(false);
                if (edited is not null)
                {
                    Ui(() => SetEditorText(edited));
                }
            });
            return;
        }

        // Alt+Enter / Ctrl+Q → follow-up submit
        if (key == Key.Enter.WithAlt || key == Key.Q.WithCtrl)
        {
            key.Handled = true;
            _submitAsFollowUp = true;
            SubmitEditor();
            return;
        }

        // Alt+Up / Alt+Q → restore queues to editor
        if (key == Key.CursorUp.WithAlt || key == Key.Q.WithAlt)
        {
            key.Handled = true;
            var restored = _queues.DrainToEditorText();
            if (string.IsNullOrEmpty(restored))
            {
                AppendSystem("queue empty");
                return;
            }

            SetEditorText(restored);
            AppendSystem("restored queued messages to editor");
        }
    }

    private async Task CycleModelAsync(int direction)
    {
        if (Volatile.Read(ref _turnBusy) != 0)
        {
            Ui(() => AppendSystem("busy — wait for the current turn"));
            return;
        }

        var next = await _host.CycleModelAsync(direction, _cancel.AppToken).ConfigureAwait(false);
        Ui(() =>
        {
            AppendSystem($"model → {next}");
            RefreshStatus();
        });
    }

    private async Task RunHotkeyAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Ui(() => AppendSystem("error: " + ex.Message));
        }
        finally
        {
            Ui(() => _input.SetFocus());
        }
    }

    private void OnInputAccepting(object? sender, CommandEventArgs e)
    {
        e.Handled = true;
        SubmitEditor(forceFollowUp: false);
    }

    private void SubmitEditor(bool forceFollowUp = false)
    {
        var asFollowUp = forceFollowUp || _submitAsFollowUp;
        _submitAsFollowUp = false;

        var line = GetEditorText().TrimEnd();
        SetEditorText(string.Empty);

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (IsExitCommand(line.Trim()))
        {
            App?.RequestStop();
            return;
        }

        if (Volatile.Read(ref _turnBusy) != 0)
        {
            var item = SteerInput.Parse(asFollowUp ? "+ " + line.Trim() : line);
            if (item is null)
            {
                return;
            }

            if (item.Value.Kind == SteerInput.Kind.FollowUp || asFollowUp)
            {
                _queues.EnqueueFollowUp(item.Value.Text);
                AppendSystem($"follow-up queued ({_queues.FollowUpCount})");
                return;
            }

            // Pi Enter-while-busy: steer — cancel turn, then rerun with this text.
            _queues.EnqueueSteer(item.Value.Text);
            AppendSystem($"steer queued — cancelling turn");
            _cancel.CancelTurn();
            return;
        }

        if (asFollowUp)
        {
            // Idle Alt+Enter: treat as a normal turn (Pi still submits).
            _ = RunUserLineAsync(line);
            return;
        }

        _ = RunUserLineAsync(line);
    }

    private async Task DrainQueuedInputsAsync()
    {
        if (_queues.TryDequeueSteer(out var steer))
        {
            Ui(() => AppendSystem("applying steer"));
            await RunUserLineAsync(steer).ConfigureAwait(false);
            return;
        }

        if (_queues.TryDequeueFollowUp(out var follow))
        {
            Ui(() => AppendRole("follow", follow));
            await RunUserLineAsync(follow).ConfigureAwait(false);
        }
    }

    private static bool IsExitCommand(string line) =>
        line.Equals("/exit", StringComparison.OrdinalIgnoreCase)
        || line.Equals("/quit", StringComparison.OrdinalIgnoreCase)
        || line.Equals("exit", StringComparison.OrdinalIgnoreCase);
}
