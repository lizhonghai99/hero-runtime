using System;
using System.Collections.Generic;
using System.Linq;

namespace Hero.Cli;

internal sealed class CliArgs
{
    public bool Continue { get; private init; }

    public bool Help { get; private init; }

    public bool NoHarness { get; private init; }

    /// <summary>When set, run one turn and exit (print mode).</summary>
    public string? PrintPrompt { get; private init; }

    /// <summary>When set (including empty path), export session tree and exit.</summary>
    public string? ExportPath { get; private init; }

    public bool ExportRequested { get; private init; }

    /// <summary>When exporting, emit self-contained HTML (session + traces).</summary>
    public bool ExportHtml { get; private init; }

    /// <summary>off | shell | all | auto — overrides settings for this process.</summary>
    public string? ToolApproval { get; private init; }

    /// <summary>
    /// Explicit automation: auto-approve tool prompts (ApproveOnce). Fail-closed otherwise.
    /// </summary>
    public bool Yes { get; private init; }

    /// <summary>Print mode: emit JSONL agent events on stdout (requires <c>-p</c>).</summary>
    public bool Json { get; private init; }

    /// <summary>
    /// Explicitly trust project <c>.hero-agent/mcp.json</c> for this process
    /// (persists fingerprint to <c>~/.hero-agent/trusted-mcp.json</c>).
    /// </summary>
    public bool TrustProjectMcp { get; private init; }

    public static CliArgs Parse(string[] args)
    {
        var cont = false;
        var help = false;
        var noHarness = false;
        var yes = false;
        var json = false;
        var trustProjectMcp = false;
        string? printPrompt = null;
        string? exportPath = null;
        var exportRequested = false;
        var exportHtml = false;
        string? toolApproval = null;
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-c" or "--continue")
            {
                cont = true;
            }
            else if (arg is "-h" or "--help" or "/?")
            {
                help = true;
            }
            else if (arg is "--no-harness")
            {
                noHarness = true;
            }
            else if (arg is "--yes" or "-y")
            {
                yes = true;
            }
            else if (arg is "--json")
            {
                json = true;
            }
            else if (arg is "--trust-project-mcp")
            {
                trustProjectMcp = true;
            }
            else if (arg is "--html")
            {
                exportHtml = true;
            }
            else if (arg is "--export")
            {
                exportRequested = true;
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    exportPath = args[++i];
                }
                else
                {
                    exportPath = null;
                }
            }
            else if (arg is "--tool-approval")
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--tool-approval requires off|shell|all|auto");
                }

                toolApproval = args[++i];
            }
            else if (arg is "-p" or "--print")
            {
                if (i + 1 < args.Length)
                {
                    // Allow: hi -p "…" --json  (flag after -p must still set Json).
                    var rest = new List<string>();
                    foreach (var a in args.Skip(i + 1))
                    {
                        if (a is "--json")
                        {
                            json = true;
                            continue;
                        }

                        rest.Add(a);
                    }

                    if (rest.Count == 0)
                    {
                        throw new ArgumentException("-p / --print requires a prompt. Try: hi -p \"hello\"");
                    }

                    printPrompt = string.Join(' ', rest);
                    break;
                }

                throw new ArgumentException("-p / --print requires a prompt. Try: hi -p \"hello\"");
            }
            else if (arg.StartsWith('-'))
            {
                throw new ArgumentException($"Unknown option: {arg}. Try --help.");
            }
            else if (exportRequested && exportPath is null)
            {
                // Supports: hi --export --html ./out.html
                exportPath = arg;
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (printPrompt is null && positional.Count > 0 && args.Any(a => a is "-p" or "--print"))
        {
            printPrompt = string.Join(' ', positional);
        }

        return new CliArgs
        {
            Continue = cont,
            Help = help,
            NoHarness = noHarness,
            PrintPrompt = printPrompt,
            ExportPath = exportPath,
            ExportRequested = exportRequested,
            ExportHtml = exportHtml,
            ToolApproval = toolApproval,
            Yes = yes,
            Json = json,
            TrustProjectMcp = trustProjectMcp,
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            hi — HERO CLI

            Usage:
              hi                 Start a new session for the current directory
              hi --continue      Resume the latest session tip for this directory
              hi -c              Same as --continue
              hi -p "prompt"     Print mode: one turn, stream to stdout, exit
              hi --print "…"     Same as -p
              hi -p "…" --json   Machine-readable JSONL event stream on stdout
              hi --no-harness    Disable HarnessAgent todo/mode/loop (closer to Pi-minimal)
              hi --export [path] Export tip tree to JSON+Markdown and exit
              hi --export --html [path]  Export session+traces HTML and exit
              hi --tool-approval off|shell|all|auto
                                 Default: shell (run_shell needs HITL). off = unsafe demo mode.
              hi --yes / -y      Auto-approve tool prompts (explicit; default is shell)
              hi --trust-project-mcp  Trust project .hero-agent/mcp.json (stdio spawn)

            Chat providers (DeepSeek / Moonshot):
              /model or Ctrl+L  → pick provider → API key (memory only) → live model list
              /settings         → show defaults; Shift+Tab / /model persist to settings.json
              env: DEEPSEEK_API_KEY | MOONSHOT_API_KEY
              settings (no keys): ~/.hero-agent/settings.json

            Shell override (optional):
              HERO_SHELL or AGENT_FRAMEWORK_SHELL (pwsh|powershell|cmd|bash|sh|wsl)

            REPL (Pi-aligned interaction):
              /new  /model  /reasoning  /approval  /export [--html]  /tree [id]
              /compact [turns]  /memory  /skills
              /mode  /todos  /trace  /mcp  /status  /settings  /hotkeys  /help  /exit
              !cmd (AI sees)  !!cmd (AI blind)  @file (under cwd)
              Enter send · busy Enter = steer (cancel+rerun) · Alt+Enter/Ctrl+Q follow-up
              Shift+Enter newline · Esc abort+restore queues · Ctrl+C clear/quit
              Ctrl+L model · Ctrl+P cycle model · Shift+Tab reasoning · Ctrl+G editor
              approval prompt: [1] once  [2] always tool  [3] always args  [4] reject

            Data (per working directory):
              Sessions  ~/.hero-agent/sessions/<key>/current.jsonl  (tree + tip; old → archive/)
              Turn logs ~/.hero-agent/logs/<key>/turns.jsonl
              Memory    ~/.hero-agent/memory/<key>/  (Harness file_memory_* on demand)

            Docs: README.md / README-zh.md · Demo: DEMO.md / DEMO-zh.md
            """);
    }
}
