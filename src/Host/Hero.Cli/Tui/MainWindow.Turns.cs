using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Interaction;

namespace Hero.Cli.Tui;

internal partial class MainWindow
{
    private async Task RunUserLineAsync(string line)
    {
        Interlocked.Exchange(ref _turnBusy, 1);
        var trimmed = line.Trim();

        try
        {
            if (trimmed.StartsWith('!'))
            {
                await HandleBangAsync(trimmed).ConfigureAwait(false);
                return;
            }

            if (trimmed.StartsWith('/'))
            {
                await HandleSlashAsync(trimmed).ConfigureAwait(false);
                return;
            }

            if (!_host.IsChatConfigured)
            {
                Ui(() => AppendSystem("chat not configured — set DEEPSEEK_API_KEY / MOONSHOT_API_KEY"));
                return;
            }

            var cwd = _host.Options.WorkingDirectory;
            var expanded = AtFileExpander.Expand(line, cwd);
            await StreamChatTurnAsync(expanded, showYouPrefix: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Ui(() => AppendSystem($"error: {ex.Message}"));
        }
        finally
        {
            Interlocked.Exchange(ref _turnBusy, 0);
            _streamMark = -1;
            await RefreshModeAsync().ConfigureAwait(false);

            var restore = Interlocked.Exchange(ref _restoreEditorOnAbort, null);
            Ui(() =>
            {
                RefreshStatus();
                if (!string.IsNullOrEmpty(restore))
                {
                    SetEditorText(restore);
                }

                _input.SetFocus();
            });

            if (string.IsNullOrEmpty(restore))
            {
                await DrainQueuedInputsAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task HandleBangAsync(string trimmed)
    {
        var blind = trimmed.StartsWith("!!", StringComparison.Ordinal);
        var command = (blind ? trimmed[2..] : trimmed[1..]).Trim();
        if (command.Length == 0)
        {
            Ui(() => AppendSystem("usage> !cmd (AI sees)   !!cmd (AI blind)"));
            return;
        }

        Ui(() => AppendRole(blind ? "!!" : "!", command));
        try
        {
            var (code, output) = await LocalCommandRunner.RunAsync(command, _cancel.AppToken).ConfigureAwait(false);
            Ui(() =>
            {
                AppendBody(string.IsNullOrEmpty(output) ? "(no output)" : CapDisplay(output));
                AppendSystem($"exit {code}");
            });

            if (!blind)
            {
                if (!_host.IsChatConfigured)
                {
                    Ui(() => AppendSystem("chat not configured — skip AI follow-up for !cmd"));
                    return;
                }

                var payload =
                    $"""
                    I ran this shell command in the working directory and want you to use the result:

                    ```
                    {command}
                    ```

                    Exit code: {code}

                    Output:
                    ```
                    {Truncate(output, 12_000)}
                    ```
                    """;
                await StreamChatTurnAsync(payload, showYouPrefix: false).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Ui(() => AppendSystem("error: " + ex.Message));
        }
    }

    private async Task StreamChatTurnAsync(string userPayload, bool showYouPrefix)
    {
        Ui(() =>
        {
            if (showYouPrefix)
            {
                AppendRole("you", userPayload);
            }

            AppendSystem(ThinkingMarker);
            _streamMark = -1;
        });

        var turnCts = _cancel.BeginTurn();
        try
        {
            await _host.EnsureReadyAsync(turnCts.Token).ConfigureAwait(false);

            var textStarted = false;
            await foreach (var chunk in _host.RunTurnStreamingAsync(userPayload, turnCts.Token).ConfigureAwait(false))
            {
                if (chunk.Kind == AgentStreamKind.Usage)
                {
                    Ui(RefreshStatus);
                    continue;
                }

                if (chunk.Kind == AgentStreamKind.Tool)
                {
                    Ui(() => AppendTool(chunk.Text));
                    continue;
                }

                if (chunk.Kind == AgentStreamKind.Approval)
                {
                    Ui(() => AppendSystem("approval  " + chunk.Text));
                    continue;
                }

                if (chunk.Kind != AgentStreamKind.Text || string.IsNullOrEmpty(chunk.Text))
                {
                    continue;
                }

                var piece = chunk.Text;
                var first = !textStarted;
                textStarted = true;
                Ui(() =>
                {
                    if (first)
                    {
                        RemoveThinking();
                        EnsureBlankLine();
                        _doc.Append("hi");
                        _doc.AppendLine();
                        _doc.Append("  ");
                        _streamMark = _doc.Length;
                        _doc.Append(NormalizeNewlines(piece));
                        SyncTranscript(scroll: true);
                    }
                    else if (_streamMark >= 0)
                    {
                        _doc.Append(NormalizeNewlines(piece));
                        SyncTranscript(scroll: true);
                    }
                });
            }

            if (!textStarted && !turnCts.IsCancellationRequested)
            {
                Ui(() =>
                {
                    RemoveThinking();
                    AppendRole("hi", "(empty)");
                });
            }
            else if (textStarted)
            {
                Ui(() =>
                {
                    if (_streamMark >= 0 && _streamMark < _doc.Length)
                    {
                        var body = _doc.ToString(_streamMark, _doc.Length - _streamMark);
                        var indented = IndentBody(body);
                        _doc.Length = _streamMark;
                        _doc.Append(indented);
                        SyncTranscript(scroll: true);
                    }
                });
            }
        }
        catch (OperationCanceledException) when (!_cancel.AppToken.IsCancellationRequested)
        {
            Ui(() =>
            {
                RemoveThinking();
                AppendSystem("cancelled");
            });
        }
        finally
        {
            _cancel.EndTurn();
            Ui(RefreshStatus);
        }
    }
}
