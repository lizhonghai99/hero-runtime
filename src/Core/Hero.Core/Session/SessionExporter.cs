using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Observability;

namespace Hero.Core.Session;

/// <summary>
/// Exports the session tip tree to JSON / Markdown / self-contained HTML.
/// </summary>
public static class SessionExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record ExportResult(string? JsonPath, string? MarkdownPath, string? HtmlPath = null);

    /// <summary>
    /// Write <paramref name="tree"/> next to <paramref name="outputDirectory"/> (or a single stem path).
    /// When <paramref name="pathOrDirectory"/> ends with <c>.json</c> / <c>.md</c>, that stem is used;
    /// otherwise files are named <c>session-export-&lt;utc&gt;.json</c> / <c>.md</c> under the directory.
    /// </summary>
    public static async Task<ExportResult> ExportAsync(
        SessionTreeSnapshot tree,
        string? pathOrDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var (jsonPath, mdPath) = ResolvePaths(pathOrDirectory);
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var payload = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            workingDirectory = tree.WorkingDirectory,
            sourceFile = tree.FilePath,
            tipId = tree.TipId,
            nodes = tree.Nodes.Select(n => new
            {
                id = n.Id,
                parentId = n.ParentId,
                at = n.At,
                userPreview = n.UserPreview,
                assistantPreview = n.AssistantPreview,
            }).ToList(),
        };

        await File.WriteAllTextAsync(
                jsonPath,
                JsonSerializer.Serialize(payload, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);

        await File.WriteAllTextAsync(mdPath, BuildMarkdown(tree), cancellationToken).ConfigureAwait(false);
        return new ExportResult(jsonPath, mdPath);
    }

    public static async Task<ExportResult> ExportHtmlAsync(
        SessionTreeSnapshot tree,
        IReadOnlyList<TurnTrace> traces,
        string? pathOrDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        traces ??= [];

        var htmlPath = ResolveHtmlPath(pathOrDirectory);
        var dir = Path.GetDirectoryName(htmlPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var html = RenderHtml(tree, traces);
        await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
        return new ExportResult(JsonPath: null, MarkdownPath: null, HtmlPath: htmlPath);
    }

    public static string BuildMarkdown(SessionTreeSnapshot tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var sb = new StringBuilder();
        sb.AppendLine("# HERO session export");
        sb.AppendLine();
        sb.AppendLine($"- Working directory: `{tree.WorkingDirectory}`");
        sb.AppendLine($"- Source: `{tree.FilePath}`");
        sb.AppendLine($"- Tip: `{tree.TipId ?? "(none)"}`");
        sb.AppendLine($"- Nodes: {tree.Nodes.Count}");
        sb.AppendLine();

        var byParent = tree.Nodes
            .GroupBy(n => n.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        void Walk(string? parentId, int depth)
        {
            var key = parentId ?? string.Empty;
            if (!byParent.TryGetValue(key, out var children))
            {
                return;
            }

            foreach (var node in children.OrderBy(n => n.At))
            {
                var mark = string.Equals(node.Id, tree.TipId, StringComparison.OrdinalIgnoreCase) ? "*" : "-";
                var indent = new string(' ', depth * 2);
                sb.AppendLine($"{indent}{mark} `{node.Id}` {node.UserPreview}");
                if (!string.IsNullOrWhiteSpace(node.AssistantPreview))
                {
                    sb.AppendLine($"{indent}  → {Truncate(node.AssistantPreview, 160)}");
                }

                Walk(node.Id, depth + 1);
            }
        }

        Walk(null, 0);
        return sb.ToString();
    }

    /// <summary>
    /// Pure HTML renderer: session tip tree + per-turn model/tool traces (self-contained).
    /// </summary>
    public static string RenderHtml(SessionTreeSnapshot tree, IReadOnlyList<TurnTrace> traces)
    {
        ArgumentNullException.ThrowIfNull(tree);
        traces ??= [];

        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\"/>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        sb.AppendLine("<title>HERO session export</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        var exportedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<h1>HERO session + traces</h1>");
        sb.AppendLine($"<p class=\"meta\">exported {H(exportedAt)} UTC</p>");
        sb.AppendLine($"<p class=\"meta\">cwd: <code>{H(tree.WorkingDirectory)}</code></p>");
        sb.AppendLine($"<p class=\"meta\">source: <code>{H(tree.FilePath)}</code> · tip: <code>{H(tree.TipId ?? "(none)")}</code></p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Session tree</h2>");
        sb.AppendLine("<div class=\"tree\">");
        AppendTreeHtml(sb, tree);
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section>");
        sb.AppendLine("<h2>Model / tool traces</h2>");
        if (traces.Count == 0)
        {
            sb.AppendLine("<p class=\"empty\">No traces recorded yet (run turns with Observability plugin, then export).</p>");
        }
        else
        {
            var turnIndex = 0;
            foreach (var turn in traces)
            {
                turnIndex++;
                sb.AppendLine($"<article class=\"turn\" id=\"turn-{H(turn.TurnId)}\">");
                sb.AppendLine($"<h3>Turn {turnIndex} <code>{H(turn.TurnId)}</code></h3>");
                if (turn.Calls.Count == 0)
                {
                    sb.AppendLine("<p class=\"empty\">(no model calls)</p>");
                }
                else
                {
                    foreach (var call in turn.Calls)
                    {
                        AppendCallHtml(sb, call);
                    }
                }

                sb.AppendLine("</article>");
            }
        }

        sb.AppendLine("</section>");
        sb.AppendLine("<script>");
        sb.AppendLine(Js);
        sb.AppendLine("</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendTreeHtml(StringBuilder sb, SessionTreeSnapshot tree)
    {
        var byParent = tree.Nodes
            .GroupBy(n => n.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        void Walk(string? parentId, int depth)
        {
            var key = parentId ?? string.Empty;
            if (!byParent.TryGetValue(key, out var children))
            {
                return;
            }

            foreach (var node in children.OrderBy(n => n.At))
            {
                var isTip = string.Equals(node.Id, tree.TipId, StringComparison.OrdinalIgnoreCase);
                sb.AppendLine($"<div class=\"node depth-{Math.Min(depth, 6)}{(isTip ? " tip" : string.Empty)}\">");
                sb.AppendLine($"<div class=\"node-id\"><code>{H(node.Id)}</code>{(isTip ? " <span class=\"badge\">tip</span>" : string.Empty)}</div>");
                sb.AppendLine($"<div class=\"user\">{H(node.UserPreview)}</div>");
                if (!string.IsNullOrWhiteSpace(node.AssistantPreview))
                {
                    sb.AppendLine($"<div class=\"assistant\">{H(Truncate(node.AssistantPreview!, 240))}</div>");
                }

                sb.AppendLine("</div>");
                Walk(node.Id, depth + 1);
            }
        }

        Walk(null, 0);
    }

    private static void AppendCallHtml(StringBuilder sb, ModelCallRecord call)
    {
        var agent = string.IsNullOrWhiteSpace(call.AgentName) ? "hi" : call.AgentName!;
        var isSub = !string.Equals(agent, "hi", StringComparison.OrdinalIgnoreCase);
        var css = isSub ? "call sub" : "call main";
        var msg = call.Messages.Count > 0
            ? string.Join(" · ", call.Messages.Take(3).Select(m => $"{m.Role}: {Truncate(m.Text, 80)}"))
            : "(no messages)";
        var tools = call.ToolNames.Count > 0 ? string.Join(", ", call.ToolNames) : "—";
        var duration = call.DurationMs is null ? "—" : $"{call.DurationMs:0} ms";

        sb.AppendLine($"<div class=\"{css}\">");
        sb.AppendLine($"<div class=\"call-head\"><span class=\"agent\">{H(agent)}</span> <span class=\"idx\">#{call.CallIndex}</span>");
        if (call.Streaming)
        {
            sb.AppendLine(" <span class=\"badge\">stream</span>");
        }

        if (!string.IsNullOrWhiteSpace(call.Error))
        {
            sb.AppendLine(" <span class=\"badge err\">error</span>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"row\"><span class=\"k\">messages</span><span class=\"v\">{H(msg)}</span></div>");
        sb.AppendLine($"<div class=\"row\"><span class=\"k\">tools</span><span class=\"v\"><code>{H(tools)}</code></span></div>");
        sb.AppendLine(
            $"<div class=\"row\"><span class=\"k\">tokens</span><span class=\"v\">in {call.InputTokens} · out {call.OutputTokens} · cached {call.CachedInputTokens}</span></div>");
        sb.AppendLine($"<div class=\"row\"><span class=\"k\">duration</span><span class=\"v\">{H(duration)}</span></div>");
        if (!string.IsNullOrWhiteSpace(call.Error))
        {
            sb.AppendLine($"<div class=\"row err\"><span class=\"k\">error</span><span class=\"v\">{H(call.Error)}</span></div>");
        }

        sb.AppendLine("</div>");
    }

    private static string ResolveHtmlPath(string? pathOrDirectory)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".hero-agent",
            "exports");

        if (string.IsNullOrWhiteSpace(pathOrDirectory))
        {
            return Path.Combine(defaultDir, $"session-{stamp}.html");
        }

        var path = pathOrDirectory.Trim();
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        Directory.CreateDirectory(path);
        return Path.Combine(path, $"session-{stamp}.html");
    }

    private static (string JsonPath, string MdPath) ResolvePaths(string? pathOrDirectory)
    {
        if (string.IsNullOrWhiteSpace(pathOrDirectory))
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var cwd = Environment.CurrentDirectory;
            return (
                Path.Combine(cwd, $"session-export-{stamp}.json"),
                Path.Combine(cwd, $"session-export-{stamp}.md"));
        }

        var path = pathOrDirectory.Trim();
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var stem = path[..^5];
            return (path, stem + ".md");
        }

        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var stem = path[..^3];
            return (stem + ".json", path);
        }

        var stamp2 = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(path);
        return (
            Path.Combine(path, $"session-export-{stamp2}.json"),
            Path.Combine(path, $"session-export-{stamp2}.md"));
    }

    private static string Truncate(string text, int max)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..(max - 1)] + "…";
    }

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private const string Css =
        """
        :root { --bg:#0f1419; --card:#1a2332; --text:#e7ecf3; --muted:#8b9bb4; --accent:#3d9cf0; --sub:#c9a227; --ok:#3dd68c; --err:#f07178; --line:#2a3548; }
        * { box-sizing: border-box; }
        body { margin:0; font:14px/1.5 ui-sans-serif,system-ui,Segoe UI,sans-serif; background:var(--bg); color:var(--text); padding:24px; }
        header.hero { margin-bottom:28px; border-bottom:1px solid var(--line); padding-bottom:16px; }
        h1 { margin:0 0 8px; font-size:22px; letter-spacing:.02em; }
        h2 { margin:28px 0 12px; font-size:16px; color:var(--accent); }
        h3 { margin:0 0 10px; font-size:14px; }
        .meta { margin:2px 0; color:var(--muted); }
        code { font-family:ui-monospace,Consolas,monospace; font-size:12px; }
        .tree .node { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:10px 12px; margin:8px 0; }
        .tree .depth-1 { margin-left:16px; } .tree .depth-2 { margin-left:32px; } .tree .depth-3 { margin-left:48px; }
        .tree .depth-4 { margin-left:64px; } .tree .depth-5 { margin-left:80px; } .tree .depth-6 { margin-left:96px; }
        .tree .tip { border-color:var(--accent); }
        .badge { display:inline-block; font-size:11px; padding:1px 6px; border-radius:999px; background:#243247; color:var(--accent); margin-left:6px; }
        .badge.err { color:var(--err); background:#3a1f24; }
        .user { margin-top:4px; } .assistant { margin-top:4px; color:var(--muted); }
        .turn { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px; margin:12px 0; }
        .call { border-left:3px solid var(--ok); padding:8px 10px; margin:8px 0; background:#121a24; border-radius:0 8px 8px 0; }
        .call.sub { border-left-color:var(--sub); margin-left:20px; }
        .call-head { margin-bottom:6px; }
        .agent { font-weight:600; color:var(--accent); }
        .call.sub .agent { color:var(--sub); }
        .idx { color:var(--muted); }
        .row { display:flex; gap:10px; margin:3px 0; }
        .k { width:72px; color:var(--muted); flex:0 0 auto; }
        .v { flex:1; word-break:break-word; }
        .empty { color:var(--muted); }
        .row.err .v { color:var(--err); }
        """;

    private const string Js =
        """
        document.querySelectorAll('.call-head').forEach(h => {
          h.style.cursor = 'pointer';
          h.addEventListener('click', () => {
            const body = h.parentElement;
            body.classList.toggle('collapsed');
            [...body.children].forEach((el, i) => { if (i > 0) el.style.display = body.classList.contains('collapsed') ? 'none' : ''; });
          });
        });
        """;
}
