using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;
using Hero.Core.Providers;
using Hero.Core.Session;

namespace Hero.Core.Hosting;

/// <summary>
/// Host-agnostic built-in slash command logic. Hosts only render <see cref="BuiltinSlashResult.Lines"/>
/// and honor <see cref="BuiltinSlashResult.Action"/> (e.g. open a model picker UI).
/// </summary>
public sealed class BuiltinSlashCommands
{
    private readonly AgentHost _host;

    public BuiltinSlashCommands(AgentHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Handle a built-in slash line. Returns null when the line is not a built-in (plugins may handle it).
    /// </summary>
    public async Task<BuiltinSlashResult?> TryHandleAsync(
        string line,
        IEnumerable<ISlashCommand>? pluginCommands = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith('/'))
        {
            return null;
        }

        var trimmed = line.Trim();
        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0];
        var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        if (cmd.Equals("/help", StringComparison.OrdinalIgnoreCase))
        {
            return BuiltinSlashResult.LinesOnly(HelpLines(pluginCommands));
        }

        if (cmd.Equals("/hotkeys", StringComparison.OrdinalIgnoreCase))
        {
            return BuiltinSlashResult.LinesOnly(
            [
                "Enter                 submit (idle) · steer cancel+rerun (busy)",
                "Alt+Enter / Ctrl+Q    follow-up after current turn",
                "Shift+Enter / Ctrl+J  newline",
                "Esc                   abort turn · restore queued text (busy)",
                "Alt+Up / Alt+Q        restore queued messages to editor",
                "Ctrl+C                clear editor · empty = quit",
                "Ctrl+L                model picker",
                "Ctrl+P / Alt+P        cycle model forward / backward",
                "Shift+Tab             cycle reasoning",
                "Ctrl+G                external editor",
                "!cmd / !!cmd          shell · @path attach file under cwd",
            ]);
        }

        if (cmd.Equals("/status", StringComparison.OrdinalIgnoreCase))
        {
            var model = _host.GetStatusModelInfo();
            var usage = _host.Usage.Snapshot();
            return BuiltinSlashResult.LinesOnly(
            [
                $"↑{FormatCount(usage.InputTokens)} ↓{FormatCount(usage.OutputTokens)} · " +
                $"{model.Provider} · {model.Model} · {model.ReasoningLevel}",
            ]);
        }

        if (cmd.Equals("/memory", StringComparison.OrdinalIgnoreCase))
        {
            var root = _host.MemoryDirectory;
            var files = _host.ListMemoryFiles();
            var lines = new List<string>
            {
                $"memory> {root}",
                "note> Harness file_memory_* tools (on demand). Same tier as sessions/ and logs/.",
            };
            if (files.Count == 0)
            {
                lines.Add("(empty — agent can write notes with file_memory_write)");
            }
            else
            {
                lines.Add($"files> {files.Count}");
                lines.AddRange(files.Select(f => "  " + f));
            }

            return BuiltinSlashResult.LinesOnly(lines);
        }

        if (cmd.Equals("/compact", StringComparison.OrdinalIgnoreCase))
        {
            var keepTurns = 6;
            if (!string.IsNullOrWhiteSpace(arg))
            {
                if (!int.TryParse(arg, out keepTurns) || keepTurns < 1)
                {
                    return BuiltinSlashResult.LinesOnly(
                    [
                        "compact> usage: /compact [keepTurns]",
                        "example> /compact 6  (collapse tool results + keep last 6 user turns)",
                    ]);
                }
            }

            var result = await _host.CompactTranscriptAsync(keepTurns, cancellationToken).ConfigureAwait(false);
            if (result.MessagesBefore == 0)
            {
                return BuiltinSlashResult.LinesOnly(["compact> (empty transcript — nothing to compact)"]);
            }

            return BuiltinSlashResult.LinesOnly(
            [
                result.Changed
                    ? $"compact> {result.MessagesBefore} → {result.MessagesAfter} messages (kept last {result.KeptTurns} turns)"
                    : $"compact> no change ({result.MessagesAfter} messages, already within last {result.KeptTurns} turns)",
                "note> live context only; next turn persists the compacted history",
            ]);
        }

        if (cmd.Equals("/skills", StringComparison.OrdinalIgnoreCase))
        {
            var names = _host.Skills.ListSkillNames();
            var lines = new List<string>
            {
                $"skills> catalog only — bodies load via load_skill (on demand)",
                $"roots> project={_host.Skills.ProjectSkillCount}  plugin={_host.Skills.PluginSkillCount}  user={_host.Skills.UserSkillCount}",
            };
            if (names.Count == 0)
            {
                lines.Add("(none — put SKILL.md under {cwd}/.hero-agent/skills/<name>/)");
            }
            else
            {
                lines.AddRange(names.Select(n => "  " + n));
            }

            return BuiltinSlashResult.LinesOnly(lines);
        }

        if (cmd.Equals("/new", StringComparison.OrdinalIgnoreCase))
        {
            await _host.BeginNewSessionAsync(cancellationToken).ConfigureAwait(false);
            return new BuiltinSlashResult
            {
                Handled = true,
                Lines = ["session> new (previous archived)"],
                Action = BuiltinSlashAction.ClearLocalQueues,
                RefreshStatus = true,
            };
        }

        if (cmd.Equals("/settings", StringComparison.OrdinalIgnoreCase))
        {
            var settings = await _host.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            var live = _host.GetStatusModelInfo();
            return BuiltinSlashResult.LinesOnly(
            [
                $"file> {_host.SettingsStore.FilePath}",
                $"defaultProvider> {settings.DefaultProvider ?? "(unset)"}",
                $"defaultModel> {settings.DefaultModel ?? "(unset)"}",
                $"defaultThinkingLevel> {settings.DefaultThinkingLevel ?? "(unset)"}",
                $"toolApproval> {settings.ToolApproval ?? _host.GetToolApprovalMode()}",
                $"session now> {live.Provider} · {live.Model} · {live.ReasoningLevel}",
                "note> API keys are never stored (env or /model session input only)",
            ]);
        }

        if (cmd.Equals("/approval", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/approval ", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                return BuiltinSlashResult.LinesOnly(
                [
                    $"approval> {_host.GetToolApprovalMode()}",
                    "usage> /approval off|shell|all|auto  (restart hi to rebind tools)",
                    "note> headless/default DI is fail-closed; use --yes or auto to auto-approve",
                ]);
            }

            await _host.SetToolApprovalAsync(arg, cancellationToken).ConfigureAwait(false);
            return BuiltinSlashResult.LinesOnly(
            [
                $"approval> {_host.GetToolApprovalMode()}",
                "note> saved to settings.json — restart hi so tools rebind",
            ]);
        }

        if (cmd.Equals("/export", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/export ", StringComparison.OrdinalIgnoreCase))
        {
            var (html, path) = SessionExportArgs.Parse(trimmed);
            var exported = await _host.ExportSessionAsync(path, html, cancellationToken).ConfigureAwait(false);
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(exported.HtmlPath))
            {
                lines.Add("export> " + exported.HtmlPath);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(exported.JsonPath))
                {
                    lines.Add("export> " + exported.JsonPath);
                }

                if (!string.IsNullOrWhiteSpace(exported.MarkdownPath))
                {
                    lines.Add("export> " + exported.MarkdownPath);
                }
            }

            return new BuiltinSlashResult
            {
                Handled = true,
                Lines = lines,
                MarkdownPreviewPath = exported.MarkdownPath,
            };
        }

        if (cmd.Equals("/model", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/model ", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                return new BuiltinSlashResult
                {
                    Handled = true,
                    Action = BuiltinSlashAction.OpenModelPicker,
                    RefreshStatus = true,
                };
            }

            var asProvider = LlmProviderCatalog.Find(arg);
            if (asProvider is not null)
            {
                return new BuiltinSlashResult
                {
                    Handled = true,
                    Action = BuiltinSlashAction.OpenModelPicker,
                    PreferredProviderId = asProvider.Id,
                    RefreshStatus = true,
                };
            }

            await _host.SetModelAsync(arg, cancellationToken).ConfigureAwait(false);
            return new BuiltinSlashResult
            {
                Handled = true,
                Lines = [$"model> {arg}"],
                RefreshStatus = true,
            };
        }

        if (cmd.Equals("/reasoning", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/reasoning ", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                var next = await _host.CycleReasoningAsync(cancellationToken).ConfigureAwait(false);
                return new BuiltinSlashResult
                {
                    Handled = true,
                    Lines = [$"reasoning> {next}"],
                    RefreshStatus = true,
                };
            }

            await _host.SetReasoningAsync(arg, cancellationToken).ConfigureAwait(false);
            return new BuiltinSlashResult
            {
                Handled = true,
                Lines = [$"reasoning> {_host.GetStatusModelInfo().ReasoningLevel}"],
                RefreshStatus = true,
            };
        }

        if (cmd.Equals("/tree", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/tree ", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(arg))
            {
                var id = await _host.CheckoutSessionNodeAsync(arg, cancellationToken).ConfigureAwait(false);
                return BuiltinSlashResult.LinesOnly(
                [
                    $"tree> checked out {id} — next message branches from here",
                ]);
            }

            var tree = await _host.GetSessionTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree.Nodes.Count == 0)
            {
                return BuiltinSlashResult.LinesOnly(["(empty session tree — send a message first)"]);
            }

            var treeLines = SessionTreeFormatter.FormatLines(tree).ToList();
            treeLines.Add("tip> /tree <id>  checkout and branch from that node");
            return BuiltinSlashResult.LinesOnly(treeLines);
        }

        return null;
    }

    public static IReadOnlyList<string> HelpLines(IEnumerable<ISlashCommand>? pluginCommands)
    {
        var lines = new List<string>
        {
            "/new                  archive current session and start fresh",
            "/model [provider|id]  provider → API key → live model list",
            "/reasoning [level]    none|low|medium|high (cycles when omitted)",
            "/approval [mode]      off|shell|all|auto (saved; restart hi to rebind tools)",
            "/export [path]        export tip tree to JSON + Markdown",
            "/export --html [path] export session+traces as self-contained HTML",
            "/settings             show ~/.hero-agent/settings.json defaults",
            "/tree [id]            list session tree or checkout a node",
            "/status               print usage status line",
            "/memory               file-memory path + notes (~/.hero-agent/memory/<cwd-key>/)",
            "/skills               skill catalog names (bodies via load_skill on demand)",
            "/compact [turns]      manual compact (tool collapse + keep last N turns; default 6)",
            "/hotkeys              interactive TUI key map (Pi-aligned)",
            "/trace                last turn tool/LLM trace (observability plugin)",
            "/help                 this help",
            "/exit                 quit",
            "!cmd / !!cmd          run shell in cwd (AI sees / AI blind)",
            "@path                 attach file under cwd (on submit)",
            "(during a turn)       Enter = steer (cancel+rerun); Alt+Enter / +text = follow-up",
        };

        if (pluginCommands is not null)
        {
            foreach (var plugin in pluginCommands)
            {
                lines.Add($"/{plugin.Name,-19} {plugin.Description}");
            }
        }

        return lines;
    }

    private static string FormatCount(long n) =>
        n < 1_000 ? n.ToString() : $"{n / 1000.0:0.#}k";
}

public enum BuiltinSlashAction
{
    None = 0,
    OpenModelPicker,
    ClearLocalQueues,
}

public sealed class BuiltinSlashResult
{
    public bool Handled { get; init; }

    public IReadOnlyList<string> Lines { get; init; } = [];

    public BuiltinSlashAction Action { get; init; }

    public string? PreferredProviderId { get; init; }

    public bool RefreshStatus { get; init; }

    /// <summary>Optional markdown export path for hosts that want a short preview.</summary>
    public string? MarkdownPreviewPath { get; init; }

    public static BuiltinSlashResult LinesOnly(IReadOnlyList<string> lines) =>
        new() { Handled = true, Lines = lines };
}
