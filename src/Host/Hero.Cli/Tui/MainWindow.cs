using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Hosting;
using Hero.Core.Plugins;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Hero.Cli.Tui;

/// <summary>
/// Interactive HERO shell with Pi-aligned editor / steer / follow-up semantics.
/// Agent work stays on <see cref="AgentHost"/>.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private const string ThinkingMarker = "··· thinking";
    private const int DisplayCap = 48_000;
    private const int EditorHeight = 3;

    private readonly AgentHost _host;
    private readonly PluginCatalog _catalog;
    private readonly ConsoleCancelScope _cancel;
    private readonly TuiDialogService _dialogs;
    private readonly BuiltinSlashCommands _slash;
    private readonly string _startupBanner;
    private readonly StringBuilder _doc = new();
#pragma warning disable CS0618 // TextView obsolete; used for transcript + editor.
    private readonly TextView _transcript;
    private readonly TextView _input;
#pragma warning restore CS0618
    private readonly Label _status;
    private readonly Label _hint;
    private readonly SteerQueue _queues = new();
    private int _turnBusy;
    private bool _started;
    private int _streamMark = -1;
    private string _modeLabel = "—";
    private string? _restoreEditorOnAbort;
    private bool _submitAsFollowUp;

    public MainWindow(
        AgentHost host,
        PluginCatalog catalog,
        ConsoleCancelScope cancel,
        TuiDialogService dialogs,
        string startupBanner)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _slash = new BuiltinSlashCommands(host);
        _startupBanner = startupBanner ?? string.Empty;
        _cancel.TuiOwnsCtrlC = true;

        Title = " hi ";
        BorderStyle = LineStyle.Rounded;
        SchemeName = "HeroChrome";

        // Footer: status (1) + compose frame (EditorHeight+2 for border) — no nested chat frame.
        // Hint is folded into the status bar to reduce chrome height.
        var composeRows = EditorHeight + 2; // border top/bottom
        var footerRows = 1 + composeRows;

#pragma warning disable CS0618
        _transcript = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(footerRows),
            ReadOnly = true,
            WordWrap = true,
            CanFocus = true,
            SchemeName = "HeroTranscript",
        };

        _status = new Label
        {
            Text = " starting… ",
            X = 0,
            Y = Pos.AnchorEnd(footerRows),
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = "HeroStatus",
        };

        var compose = new FrameView
        {
            Title = " input ",
            X = 0,
            Y = Pos.AnchorEnd(composeRows),
            Width = Dim.Fill(),
            Height = composeRows,
            BorderStyle = LineStyle.Single,
            SchemeName = "HeroCompose",
        };

        var prompt = new Label
        {
            Text = "❯",
            X = 0,
            Y = 0,
            Width = 2,
            SchemeName = "HeroPrompt",
        };

        _input = new TextView
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true,
            CanFocus = true,
            SchemeName = "HeroInput",
        };
#pragma warning restore CS0618

        // Hint label kept for layout API compatibility; content lives in status bar.
        _hint = new Label { Visible = false };

        // Enter submits; Shift+Enter / Ctrl+J keep TextView newline bindings.
        _input.KeyBindings.Remove(Key.Enter);
        _input.KeyBindings.Add(Key.Enter, Command.Accept);
        _input.Accepting += OnInputAccepting;
        KeyDown += OnWindowKeyDown;

        compose.Add(prompt, _input);
        Add(_transcript, _status, compose);
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (!newIsRunning || _started)
        {
            return;
        }

        _started = true;
        if (App is not null)
        {
            _dialogs.Attach(App);
        }

        AppendSystem("ready — type a message  ·  /help  ·  /hotkeys");
        AppendSystem("warming up…");
        RefreshStatus();
        _ = BootstrapAsync();
    }

    private async Task BootstrapAsync()
    {
        try
        {
            await _catalog.WarmupAsync(_cancel.AppToken).ConfigureAwait(false);
            if (_host.IsChatConfigured)
            {
                await _host.EnsureReadyAsync(_cancel.AppToken).ConfigureAwait(false);
            }

            await RefreshModeAsync().ConfigureAwait(false);
            Ui(() =>
            {
                ReplaceLastSystem("ready");
                RefreshStatus();
                _input.SetFocus();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Ui(() =>
            {
                ReplaceLastSystem($"warmup error: {ex.Message}");
                RefreshStatus();
            });
        }
    }

    private void RefreshStatus()
    {
        var usage = _host.Usage.Snapshot();
        var model = _host.GetStatusModelInfo();
        var approval = _host.GetToolApprovalMode();
        var busy = Volatile.Read(ref _turnBusy) != 0 ? "  busy" : string.Empty;
        var q = _queues.TotalCount > 0 ? $"  q:{_queues.TotalCount}" : string.Empty;
        // Compact Pi-like status: tokens · provider/model · think · mode · approval
        _status.Text =
            $" ↑{FormatCount(usage.InputTokens)} ↓{FormatCount(usage.OutputTokens)}" +
            $"  {model.Provider}/{model.Model}" +
            $"  think:{model.ReasoningLevel}" +
            $"  {_modeLabel}" +
            $"  approval:{approval}{busy}{q}" +
            "   | Enter send  Alt+Enter follow  Esc abort  /hotkeys ";
        Title = $" hi · {model.Provider}/{model.Model} · {_modeLabel} ";
    }

    private string GetEditorText() => _input.Text?.ToString() ?? string.Empty;

    private void SetEditorText(string text)
    {
        _input.Text = text ?? string.Empty;
        try
        {
            _input.MoveEnd();
        }
        catch
        {
            // ignore
        }
    }

    private void AppendRole(string role, string body)
    {
        EnsureBlankLine();
        _doc.Append(role);
        _doc.AppendLine();
        _doc.Append("  ");
        _doc.Append(IndentBody(CapDisplay(body)));
        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void AppendBody(string body)
    {
        EnsureBlankLine();
        _doc.Append("  ");
        _doc.Append(IndentBody(CapDisplay(body)));
        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void AppendTool(string text)
    {
        EnsureBlankLine();
        _doc.Append("⚙ ");
        _doc.Append(IndentBody(CapDisplay(text), "   "));
        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void AppendSystem(string text)
    {
        // System lines stay dense (no blank row between · lines).
        if (_doc.Length > 0 && _doc[^1] != '\n')
        {
            _doc.AppendLine();
        }

        _doc.Append("· ");
        _doc.Append(IndentBody(CapDisplay(text), "  "));
        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void AppendHelpBlock(string title, IReadOnlyList<string> lines)
    {
        EnsureBlankLine();
        _doc.Append("── ").Append(title).AppendLine(" ──");
        foreach (var line in lines)
        {
            var sep = line.IndexOf("  ", StringComparison.Ordinal);
            if (sep > 0)
            {
                var key = line[..sep].TrimEnd();
                var desc = line[(sep + 2)..].TrimStart();
                _doc.Append("  ");
                _doc.Append(key.PadRight(22));
                _doc.Append(' ');
                _doc.AppendLine(desc);
            }
            else
            {
                _doc.Append("  ").AppendLine(line);
            }
        }

        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void ReplaceLastSystem(string text)
    {
        var content = _doc.ToString();
        var idx = content.LastIndexOf("\n· ", StringComparison.Ordinal);
        if (idx < 0 && content.StartsWith("· ", StringComparison.Ordinal))
        {
            idx = 0;
        }

        if (idx < 0)
        {
            AppendSystem(text);
            return;
        }

        var start = idx == 0 ? 0 : idx + 1;
        _doc.Length = start;
        _doc.Append("· ");
        _doc.Append(IndentBody(CapDisplay(text), "  "));
        _doc.AppendLine();
        SyncTranscript(scroll: true);
    }

    private void RemoveThinking()
    {
        var content = _doc.ToString();
        var marker = "· " + ThinkingMarker;
        var idx = content.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return;
        }

        var lineStart = idx;
        while (lineStart > 0 && content[lineStart - 1] == '\n')
        {
            lineStart--;
        }

        var lineEnd = content.IndexOf('\n', idx);
        _doc.Length = lineStart;
        if (lineEnd >= 0 && lineEnd + 1 < content.Length)
        {
            _doc.Append(content[(lineEnd + 1)..]);
        }

        while (_doc.Length > 0 && _doc[^1] == '\n')
        {
            _doc.Length--;
        }

        if (_doc.Length > 0)
        {
            _doc.AppendLine();
        }

        SyncTranscript(scroll: false);
    }

    private void EnsureBlankLine()
    {
        if (_doc.Length == 0)
        {
            return;
        }

        if (_doc[^1] != '\n')
        {
            _doc.AppendLine();
        }

        if (_doc.Length >= 2 && _doc[^2] != '\n')
        {
            _doc.AppendLine();
        }
    }

    private void SyncTranscript(bool scroll)
    {
        _transcript.Text = _doc.ToString();
        if (scroll)
        {
            ScrollToEnd();
        }
    }

    private void ScrollToEnd()
    {
        try
        {
            if (_transcript.Lines <= 0)
            {
                return;
            }

            _transcript.MoveEnd();
        }
        catch
        {
            // TextView scroll APIs vary slightly across builds.
        }
    }

    private void Ui(Action action)
    {
        var app = App;
        if (app is null)
        {
            action();
            return;
        }

        app.Invoke(action);
    }

    private static string IndentBody(string text, string indent = "  ")
    {
        var normalized = NormalizeNewlines(text);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        return normalized.Replace("\n", "\n" + indent, StringComparison.Ordinal);
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string CapDisplay(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= DisplayCap)
        {
            return value;
        }

        return value[..DisplayCap] + "\n… (truncated for display; use /trace for full call)";
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }

    private static string FormatCount(long n) =>
        n < 1_000 ? n.ToString() : $"{n / 1000.0:0.#}k";
}
