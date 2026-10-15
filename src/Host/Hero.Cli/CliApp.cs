using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Approval;
using Hero.Core.Convention;
using Hero.Core.DependencyInjection;
using Hero.Core.Hosting;
using Hero.Core.Observability;
using Hero.Core.Options;
using Hero.Core.Plugins;
using Hero.Core.Settings;
using Hero.Core.Skills;
using Hero.Cli.Tui;
using Hero.Plugins.Coding;
using Hero.Plugins.DataAnalysis;
using Hero.Plugins.Harness;
using Hero.Plugins.Mcp;
using Hero.Plugins.Observability;
using Hero.Plugins.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Cli;

/// <summary>
/// Console host for <c>hi</c>. Uses Generic Host for config / DI / logging.
/// </summary>
internal sealed class CliApp
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        BannerPrinter.EnsureUtf8Console();

        CliArgs cliArgs;
        try
        {
            cliArgs = CliArgs.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            CliArgs.PrintHelp();
            return 2;
        }

        var printModeEarly = !string.IsNullOrWhiteSpace(cliArgs.PrintPrompt);
        var jsonModeEarly = printModeEarly && cliArgs.Json;
        // Unified output gate before any banner / REPL / print rendering.
        ConsoleRenderer.Initialize(jsonMode: jsonModeEarly);

        if (cliArgs.Help)
        {
            PrintBanner();
            CliArgs.PrintHelp();
            return 0;
        }

        if (cliArgs.Json && string.IsNullOrWhiteSpace(cliArgs.PrintPrompt))
        {
            Console.Error.WriteLine("--json requires -p / --print. Try: hi -p \"hello\" --json");
            return 2;
        }

        var printMode = printModeEarly;
        var jsonMode = jsonModeEarly;
        var exportMode = cliArgs.ExportRequested;
        // Interactive TUI paints its own chrome; avoid console banner fighting the alternate screen.
        if (printMode || exportMode || cliArgs.Help)
        {
            // help already printed banner above when Help; export/print keep classic console.
        }
        else if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            PrintBanner();
        }

        if (!string.IsNullOrWhiteSpace(cliArgs.ToolApproval))
        {
            // Validate early so --help-less mistakes fail before host build.
            _ = ToolApprovalModeParser.Parse(cliArgs.ToolApproval);
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        // --json / interactive TUI: never paint logs onto the console surface.
        // Print mode keeps a simple console logger for diagnostics.
        if (!jsonMode && printMode)
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        }

        if (!string.IsNullOrWhiteSpace(cliArgs.ToolApproval))
        {
            builder.Configuration["Hero:ToolApproval"] = cliArgs.ToolApproval;
        }
        else if (cliArgs.Yes)
        {
            // --yes: auto-approve tools and workflow HITL (workflow.json approve port).
            builder.Configuration["Hero:ToolApproval"] = "auto";
        }

        // Interactive REPL uses Terminal.Gui dialogs; print/export keep console / fail-closed paths.
        var useTui = !printMode && !exportMode
            && !Console.IsInputRedirected
            && !Console.IsOutputRedirected;
        var turnInput = new TurnInputBroker();
        builder.Services.AddSingleton(turnInput);
        var tuiDialogs = new TuiDialogService();
        builder.Services.AddSingleton(tuiDialogs);

        // Before AddHeroAgent so TryAddSingleton does not install the rejecting default.
        // Fail-closed by default for non-CLI hosts; CLI is interactive unless --yes / auto.
        var useAutoHandler = cliArgs.Yes
            || ToolApprovalModeParser.UsesAutoHandler(ResolveToolApprovalMode(cliArgs));
        if (useAutoHandler)
        {
            builder.Services.AddSingleton<IToolApprovalHandler, AutoApprovingToolApprovalHandler>();
        }
        else if (useTui)
        {
            builder.Services.AddSingleton<IToolApprovalHandler, TuiToolApprovalHandler>();
        }
        else
        {
            builder.Services.AddSingleton<IToolApprovalHandler, ConsoleToolApprovalHandler>();
        }

        // Project mcp.json may spawn local processes — require trust (prompt / flag / prior fingerprint).
        var allowMcpPrompt = useTui || (!printMode && !jsonMode && !Console.IsInputRedirected && !Console.IsOutputRedirected);
        if (useTui)
        {
            builder.Services.AddSingleton<IMcpProjectTrustGate>(
                new TuiMcpProjectTrustGate(
                    forceTrust: cliArgs.TrustProjectMcp,
                    allowPrompt: allowMcpPrompt,
                    dialogs: tuiDialogs));
        }
        else
        {
            builder.Services.AddSingleton<IMcpProjectTrustGate>(
                new CliMcpProjectTrustGate(
                    forceTrust: cliArgs.TrustProjectMcp,
                    allowPrompt: allowMcpPrompt,
                    input: turnInput));
        }

        builder.Services.AddHeroAgent(plugins =>
        {
            plugins.UseObservabilityPlugin();
            if (!cliArgs.NoHarness)
            {
                plugins.UseHarnessPlugin();
            }

            plugins.UseCodingPlugin()
                .UseDataAnalysisPlugin()
                .UseMcpPlugin()
                .UseWorkflowPlugin();
        });
        builder.Services.AddSingleton(new SessionRunOptions { Continue = cliArgs.Continue || exportMode });

        using var host = builder.Build();
        var catalog = host.Services.GetRequiredService<PluginCatalog>();
        using var cancel = new ConsoleCancelScope();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancel.AppToken);

        try
        {
            var chat = host.Services.GetRequiredService<IOptions<ChatProviderOptions>>().Value;
            var hero = host.Services.GetRequiredService<IOptions<HeroOptions>>().Value;
            var sessionRun = host.Services.GetRequiredService<SessionRunOptions>();
            var shell = catalog.GetMetadata(CodingPlugin.MetadataShell) ?? "n/a";
            var pluginIds = string.Join(", ", host.Services.GetServices<IPlugin>().Select(p => p.Id));
            var skills = host.Services.GetRequiredService<SkillsHost>();
            var agentHost = host.Services.GetRequiredService<AgentHost>();
            var turnLogPath = agentHost.TurnLogger.GetLogFilePath();

            if (exportMode)
            {
                await agentHost.EnsureReadyAsync(linked.Token).ConfigureAwait(false);
                var exported = await agentHost
                    .ExportSessionAsync(cliArgs.ExportPath, cliArgs.ExportHtml, linked.Token)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(exported.HtmlPath))
                {
                    Console.WriteLine($"exported> {exported.HtmlPath}");
                }
                else
                {
                    Console.WriteLine($"exported> {exported.JsonPath}");
                    Console.WriteLine($"exported> {exported.MarkdownPath}");
                }

                return 0;
            }

            if (!printMode)
            {
                var bannerText = BuildStartupBannerText(cliArgs, chat, hero, shell, pluginIds, skills, turnLogPath);

                if (useTui)
                {
                    try
                    {
                        await TuiReplHost
                            .RunAsync(agentHost, catalog, cancel, tuiDialogs, bannerText, linked.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested)
                    {
                    }

                    WriteSessionFooter(sessionRun, turnLogPath);
                    return 0;
                }

                // Redirected stdin/stdout: classic console REPL fallback.
                Console.WriteLine(bannerText);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("(warming up…)");
                Console.ResetColor();
                try
                {
                    await catalog.WarmupAsync(linked.Token).ConfigureAwait(false);
                    if (chat.IsConfigured)
                    {
                        await agentHost.EnsureReadyAsync(linked.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ConsoleRenderer.Current.WriteError($"warmup: {ex.Message}");
                }

                var repl = new RedirectedRepl(agentHost, catalog, cancel);
                try
                {
                    await repl.RunAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                }

                Console.WriteLine();
                WriteSessionFooter(sessionRun, turnLogPath);
                return 0;
            }

            // Print mode: one turn, stream to stdout, exit.
            if (jsonMode)
            {
                return await RunJsonPrintAsync(agentHost, sessionRun, chat, cliArgs.PrintPrompt!, cancel, linked.Token)
                    .ConfigureAwait(false);
            }

            if (!chat.IsConfigured)
            {
                ConsoleRenderer.Current.WriteError(
                    "chat not configured — set provider env (e.g. DEEPSEEK_API_KEY) or run /model");
                return 2;
            }

            var turnCts = cancel.BeginTurn();
            using var printLinked = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, turnCts.Token);
            try
            {
                await foreach (var chunk in agentHost
                    .RunTurnStreamingAsync(cliArgs.PrintPrompt!, printLinked.Token)
                    .ConfigureAwait(false))
                {
                    if (chunk.Kind == AgentStreamKind.Text && !string.IsNullOrEmpty(chunk.Text))
                    {
                        // Streaming: plain stdout only (no markdown rewrite).
                        await Console.Out.WriteAsync(chunk.Text).ConfigureAwait(false);
                    }
                    else if (chunk.Kind == AgentStreamKind.Tool)
                    {
                        ConsoleRenderer.Current.WriteTool(chunk.Text);
                    }
                    else if (chunk.Kind == AgentStreamKind.Approval)
                    {
                        ConsoleRenderer.Current.WriteApproval(chunk.Text);
                    }
                }

                await Console.Out.WriteLineAsync().ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("(cancelled)");
                return 130;
            }
            catch (Exception ex)
            {
                ConsoleRenderer.Current.WriteError(ex.Message);
                return 1;
            }
            finally
            {
                cancel.EndTurn();
            }
        }
        finally
        {
            await catalog.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Print + --json: every event is one JSON line on stdout; no human stderr chatter.
    /// </summary>
    private static async Task<int> RunJsonPrintAsync(
        AgentHost agentHost,
        SessionRunOptions sessionRun,
        ChatProviderOptions chat,
        string prompt,
        ConsoleCancelScope cancel,
        CancellationToken appToken)
    {
        var sw = Stopwatch.StartNew();
        long input = 0, output = 0, total = 0, cached = 0;

        if (!chat.IsConfigured)
        {
            await Console.Out.WriteLineAsync(JsonEventWriter.Error(
                    "chat not configured — set provider env (e.g. DEEPSEEK_API_KEY) or run /model"))
                .ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonEventWriter.TurnEnd(
                    status: "error",
                    durationMs: sw.ElapsedMilliseconds,
                    inputTokens: 0,
                    outputTokens: 0,
                    totalTokens: 0,
                    sessionId: sessionRun.ActiveSessionId))
                .ConfigureAwait(false);
            return 1;
        }

        var turnCts = cancel.BeginTurn();
        using var printLinked = CancellationTokenSource.CreateLinkedTokenSource(appToken, turnCts.Token);
        try
        {
            await foreach (var chunk in agentHost
                .RunTurnStreamingAsync(prompt, printLinked.Token)
                .ConfigureAwait(false))
            {
                if (chunk.Kind == AgentStreamKind.Usage && chunk.Usage is TokenUsageDelta delta)
                {
                    JsonEventWriter.Accumulate(ref input, ref output, ref total, ref cached, delta);
                }

                var line = JsonEventWriter.FromChunk(chunk);
                if (line is not null)
                {
                    await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

            await Console.Out.WriteLineAsync(JsonEventWriter.TurnEnd(
                    status: "ok",
                    durationMs: sw.ElapsedMilliseconds,
                    inputTokens: input,
                    outputTokens: output,
                    totalTokens: total,
                    sessionId: sessionRun.ActiveSessionId))
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Out.WriteLineAsync(JsonEventWriter.Error("cancelled")).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonEventWriter.TurnEnd(
                    status: "cancelled",
                    durationMs: sw.ElapsedMilliseconds,
                    inputTokens: input,
                    outputTokens: output,
                    totalTokens: total,
                    sessionId: sessionRun.ActiveSessionId))
                .ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync(JsonEventWriter.Error(ex.Message)).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(JsonEventWriter.TurnEnd(
                    status: "error",
                    durationMs: sw.ElapsedMilliseconds,
                    inputTokens: input,
                    outputTokens: output,
                    totalTokens: total,
                    sessionId: sessionRun.ActiveSessionId))
                .ConfigureAwait(false);
            return 1;
        }
        finally
        {
            cancel.EndTurn();
        }
    }

    private static string BuildStartupBannerText(
        CliArgs cliArgs,
        ChatProviderOptions chat,
        HeroOptions hero,
        string shell,
        string pluginIds,
        SkillsHost skills,
        string? turnLogPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("hi — HERO (Terminal.Gui)");
        sb.AppendLine(
            chat.IsConfigured
                ? $"chat    {chat.Provider} · {chat.Model}"
                : "chat    not configured — set DEEPSEEK_API_KEY / MOONSHOT_API_KEY");
        sb.AppendLine($"shell   {shell}");
        sb.AppendLine($"cwd     {hero.WorkingDirectory}");
        if (LooksLikeBuildOutputDirectory(hero.WorkingDirectory))
        {
            sb.AppendLine("hint    cwd looks like bin/Debug|Release — prefer repo / project root");
        }

        sb.AppendLine($"plugins {pluginIds}");
        var systemSrc = ConventionPromptStore.ResolveSystemPromptSource(hero);
        sb.AppendLine(
            systemSrc is null
                ? "prompts SYSTEM.md missing · using Hero:Instructions"
                : $"prompts {systemSrc}");
        sb.AppendLine(
            $"skills  project={skills.ProjectSkillCount}  plugin={skills.PluginSkillCount}  user={skills.UserSkillCount}  (load_skill on demand)");
        sb.AppendLine($"memory  {ConventionPaths.UserMemoryRoot(hero.WorkingDirectory)}");
        sb.AppendLine(
            cliArgs.Continue
                ? "session resume tip for this directory (if any)"
                : "session new (archives previous current.jsonl)");
        var approvalMode = ToolApprovalModeParser.Parse(hero.ToolApproval);
        sb.AppendLine($"approval {ToolApprovalModeParser.ToSettingsValue(approvalMode)}");
        if (approvalMode == ToolApprovalMode.Off)
        {
            sb.AppendLine("hint    toolApproval=off — run_shell without confirmation (demo/unsafe)");
        }

        sb.AppendLine(
            $"compact {(string.IsNullOrWhiteSpace(hero.CompactionMode) ? "auto" : hero.CompactionMode.Trim().ToLowerInvariant())}");
        if (!string.IsNullOrWhiteSpace(turnLogPath))
        {
            sb.AppendLine($"turns   {turnLogPath}");
        }

        return sb.ToString().TrimEnd();
    }

    private static void WriteSessionFooter(SessionRunOptions sessionRun, string? turnLogPath)
    {
        if (!string.IsNullOrWhiteSpace(sessionRun.SessionFilePath))
        {
            BannerPrinter.WriteKv(
                "session",
                sessionRun.Resumed
                    ? $"resumed {sessionRun.ActiveSessionId} @ {sessionRun.SessionFilePath}"
                    : $"saved {sessionRun.ActiveSessionId} @ {sessionRun.SessionFilePath}");
        }

        if (!string.IsNullOrWhiteSpace(turnLogPath))
        {
            BannerPrinter.WriteKv("turns", turnLogPath);
        }
    }

    private static void PrintBanner()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        BannerPrinter.WriteTitle(version);
    }

    /// <summary>
    /// True when cwd looks like a build output folder (e.g. bin/Debug/net10.0), where
    /// project SYSTEM.md / AGENTS.md usually are missing.
    /// </summary>
    internal static bool LooksLikeBuildOutputDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return false;
        }

        var path = workingDirectory.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var markers = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}debug{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}release{Path.DirectorySeparatorChar}",
        };

        foreach (var marker in markers)
        {
            if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return name.StartsWith("net", StringComparison.OrdinalIgnoreCase)
               && name.IndexOf('.') >= 0
               && path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>CLI flag wins; otherwise peek settings.json (before DI) for <c>auto</c>.</summary>
    private static ToolApprovalMode ResolveToolApprovalMode(CliArgs cliArgs)
    {
        if (!string.IsNullOrWhiteSpace(cliArgs.ToolApproval))
        {
            return ToolApprovalModeParser.Parse(cliArgs.ToolApproval);
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".hero-agent",
                "settings.json");
            if (!File.Exists(path))
            {
                return ToolApprovalMode.Shell;
            }

            var doc = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (string.IsNullOrWhiteSpace(doc?.ToolApproval))
            {
                return ToolApprovalMode.Shell;
            }

            return ToolApprovalModeParser.Parse(doc.ToolApproval);
        }
        catch (IOException)
        {
            return ToolApprovalMode.Shell;
        }
        catch (JsonException)
        {
            return ToolApprovalMode.Shell;
        }
        catch (UnauthorizedAccessException)
        {
            return ToolApprovalMode.Shell;
        }
        catch (ArgumentException)
        {
            return ToolApprovalMode.Shell;
        }
    }
}
