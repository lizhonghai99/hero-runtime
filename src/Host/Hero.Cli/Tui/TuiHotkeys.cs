using System.Collections.Generic;

namespace Hero.Cli.Tui;

/// <summary>English hotkey help for the interactive TUI (Pi-aligned).</summary>
internal static class TuiHotkeys
{
    public static IReadOnlyList<string> Lines { get; } =
    [
        "Enter                  submit / steer (busy)",
        "Alt+Enter / Ctrl+Q     follow-up after turn",
        "Shift+Enter / Ctrl+J   newline",
        "Esc                    abort + restore queue",
        "Alt+Up / Alt+Q         restore queue to editor",
        "Ctrl+C                 clear editor / quit",
        "Ctrl+L                 model picker",
        "Ctrl+P / Alt+P         cycle model",
        "Shift+Tab              cycle reasoning",
        "Ctrl+G                 external editor",
        "/mode                  plan | execute",
        "!cmd / !!cmd           shell (see / blind)",
        "@path                  attach file under cwd",
        "/exit                  quit",
    ];
}
