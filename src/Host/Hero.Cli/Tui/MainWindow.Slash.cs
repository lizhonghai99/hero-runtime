using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Hosting;
using Hero.Core.Plugins;
using Terminal.Gui.App;

namespace Hero.Cli.Tui;

internal partial class MainWindow
{
    private async Task HandleSlashAsync(string line)
    {
        var cmd = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        if (cmd.Equals("/exit", StringComparison.OrdinalIgnoreCase)
            || cmd.Equals("/quit", StringComparison.OrdinalIgnoreCase))
        {
            App?.RequestStop();
            return;
        }

        if (cmd.Equals("/hotkeys", StringComparison.OrdinalIgnoreCase))
        {
            Ui(() => AppendHelpBlock("hotkeys", TuiHotkeys.Lines));
            return;
        }

        if (cmd.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            Ui(() =>
            {
                foreach (var row in _startupBanner.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendSystem(row.TrimEnd());
                }

                AppendSystem(_status.Text?.ToString() ?? string.Empty);
            });
            return;
        }

        if (cmd.Equals("/new", StringComparison.OrdinalIgnoreCase))
        {
            await _host.BeginNewSessionAsync(_cancel.AppToken).ConfigureAwait(false);
            _queues.ClearAll();
            Ui(() =>
            {
                _doc.Clear();
                SyncTranscript(scroll: false);
                AppendSystem("new session");
            });
            await RefreshModeAsync().ConfigureAwait(false);
            Ui(RefreshStatus);
            return;
        }

        if (cmd.Equals("/mode", StringComparison.OrdinalIgnoreCase))
        {
            await HandleModeAsync(line).ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await _slash
                .TryHandleAsync(line, _catalog.SlashCommands, _cancel.AppToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                if (result.Action == BuiltinSlashAction.OpenModelPicker)
                {
                    await new TuiModelPicker(_host, _dialogs, msg => Ui(() => AppendSystem(msg)))
                        .RunAsync(_cancel.AppToken, result.PreferredProviderId)
                        .ConfigureAwait(false);
                }
                else
                {
                    Ui(() =>
                    {
                        foreach (var row in result.Lines)
                        {
                            AppendSystem(row);
                        }
                    });
                }

                if (result.Action == BuiltinSlashAction.ClearLocalQueues)
                {
                    _queues.ClearAll();
                }

                if (result.RefreshStatus)
                {
                    await RefreshModeAsync().ConfigureAwait(false);
                    Ui(RefreshStatus);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            Ui(() => AppendSystem("error: " + ex.Message));
            return;
        }

        var name = cmd.TrimStart('/');
        var pluginCmd = _catalog.SlashCommands.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (pluginCmd is not null)
        {
            var ctx = new TuiSlashContext(
                _host,
                msg => Ui(() => AppendSystem(msg)),
                msg => Ui(() => AppendSystem("error> " + msg)));
            await pluginCmd.TryHandleAsync(line, ctx, _cancel.AppToken).ConfigureAwait(false);
            await RefreshModeAsync().ConfigureAwait(false);
            Ui(RefreshStatus);
            return;
        }

        Ui(() => AppendSystem($"unknown: {cmd}"));
    }

    private async Task HandleModeAsync(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        const string plan = "plan";
        const string execute = "execute";

        try
        {
            string target;
            if (parts.Length >= 2)
            {
                target = parts[1].Trim();
                if (!target.Equals(plan, StringComparison.OrdinalIgnoreCase)
                    && !target.Equals(execute, StringComparison.OrdinalIgnoreCase))
                {
                    Ui(() => AppendSystem("usage> /mode [plan|execute]"));
                    return;
                }
            }
            else
            {
                var current = await _host.GetModeAsync(_cancel.AppToken).ConfigureAwait(false);
                var labels = new[]
                {
                    $"{(current.Equals(plan, StringComparison.OrdinalIgnoreCase) ? "*" : " ")} 1. plan     — design / todos (no side-effect tools)",
                    $"{(current.Equals(execute, StringComparison.OrdinalIgnoreCase) ? "*" : " ")} 2. execute  — run tools against the plan",
                };
                var selected = current.Equals(execute, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var pick = await _dialogs
                    .PickIndexAsync(
                        "Mode",
                        "Select harness mode (Esc / Cancel to abort)",
                        labels,
                        _cancel.AppToken,
                        selected)
                    .ConfigureAwait(false);
                if (pick is null)
                {
                    Ui(() => AppendSystem("mode> cancelled"));
                    return;
                }

                target = pick.Value == 1 ? execute : plan;
            }

            await _host.SetModeAsync(target, _cancel.AppToken).ConfigureAwait(false);
            var next = await _host.GetModeAsync(_cancel.AppToken).ConfigureAwait(false);
            _modeLabel = next;
            Ui(() =>
            {
                AppendSystem($"mode> {next}");
                RefreshStatus();
            });
        }
        catch (Exception ex)
        {
            Ui(() => AppendSystem("error> " + ex.Message));
        }
    }

    private async Task RefreshModeAsync()
    {
        try
        {
            _modeLabel = await _host.GetModeAsync(_cancel.AppToken).ConfigureAwait(false);
        }
        catch
        {
            _modeLabel = "—";
        }
    }

    private sealed class TuiSlashContext(
        AgentHost host,
        Action<string> writeLine,
        Action<string> writeError) : ISlashCommandContext
    {
        public IAgentRuntime Runtime { get; } = new AgentHostRuntimeAdapter(host);

        public void WriteLine(string text) => writeLine(text);

        public void WriteError(string text) => writeError(text);

        public void WriteTable(
            IReadOnlyList<string> headers,
            IReadOnlyList<IReadOnlyList<string>> rows)
        {
            writeLine(string.Join(" │ ", headers));
            foreach (var row in rows)
            {
                writeLine(string.Join(" │ ", row));
            }
        }
    }
}
