using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Core.Session;

/// <summary>
/// JSONL session log per working directory with a tip-based tree.
/// Lines: meta / turn / maf (with nodeId+parentId) / tip.
/// Older files without nodeId are treated as a linear chain.
/// </summary>
internal sealed class JsonlMafSessionStore : IMafSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly HeroOptions _options;
    private readonly ILogger<JsonlMafSessionStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _tipId;
    private string? _indexedPath;

    public JsonlMafSessionStore(IOptions<HeroOptions> options, ILogger<JsonlMafSessionStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string? CurrentTipId => _tipId;

    public string GetSessionFilePath(string workingDirectory)
    {
        var cwd = SessionPath.NormalizeWorkingDirectory(workingDirectory);
        var key = SessionPath.GetDirectoryKey(cwd);
        return Path.Combine(_options.SessionDirectory, key, "current.jsonl");
    }

    public async Task BeginNewAsync(string workingDirectory, string sessionId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSessionFilePath(workingDirectory);
            var sessionDir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(sessionDir);

            // Tree lives inside current.jsonl — never silently delete an existing tree.
            // New session = archive the old file, then start a fresh tip at this path.
            if (File.Exists(path))
            {
                var archiveDir = Path.Combine(sessionDir, "archive");
                Directory.CreateDirectory(archiveDir);
                var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
                var archivePath = Path.Combine(archiveDir, stamp + ".jsonl");
                if (File.Exists(archivePath))
                {
                    archivePath = Path.Combine(archiveDir, stamp + "-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
                }

                File.Move(path, archivePath);
                _logger.LogInformation("Archived previous session tree to {Archive}", archivePath);
            }

            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var meta = new
            {
                t = "meta",
                at = DateTimeOffset.UtcNow,
                sessionId,
                cwd = SessionPath.NormalizeWorkingDirectory(workingDirectory),
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(meta, JsonOptions)).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _tipId = null;
            _indexedPath = path;
            _logger.LogInformation("Started new session file {Path} id={SessionId}", path, sessionId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MafSessionSnapshot?> TryLoadLatestAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await ReadIndexAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            if (index.TipId is null || !index.Sessions.TryGetValue(index.TipId, out var session))
            {
                return null;
            }

            _tipId = index.TipId;
            _indexedPath = index.FilePath;

            return new MafSessionSnapshot
            {
                SessionId = index.SessionId ?? "unknown",
                WorkingDirectory = SessionPath.NormalizeWorkingDirectory(workingDirectory),
                Session = session,
                FilePath = index.FilePath,
                NodeId = index.TipId,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAsync(MafSessionAppendRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetSessionFilePath(request.WorkingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Refresh tip from disk if another path / first append after continue.
            if (!string.Equals(_indexedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                var index = await ReadIndexUnlockedAsync(path, cancellationToken).ConfigureAwait(false);
                _tipId = index.TipId;
                _indexedPath = path;
            }

            var nodeId = Guid.NewGuid().ToString("N")[..12];
            var parentId = _tipId;
            var at = DateTimeOffset.UtcNow;

            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var turn = new
            {
                t = "turn",
                at,
                sessionId = request.SessionId,
                nodeId,
                parentId,
                user = request.UserInput,
                assistant = request.AssistantOutput,
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(turn, JsonOptions)).ConfigureAwait(false);

            var maf = new
            {
                t = "maf",
                at,
                sessionId = request.SessionId,
                nodeId,
                parentId,
                session = request.Session,
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(maf, JsonOptions)).ConfigureAwait(false);

            var tip = new { t = "tip", at, nodeId };
            await writer.WriteLineAsync(JsonSerializer.Serialize(tip, JsonOptions)).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _tipId = nodeId;
            _indexedPath = path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SessionTreeSnapshot> GetTreeAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await ReadIndexAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            _tipId = index.TipId;
            _indexedPath = index.FilePath;
            return new SessionTreeSnapshot
            {
                WorkingDirectory = SessionPath.NormalizeWorkingDirectory(workingDirectory),
                FilePath = index.FilePath,
                TipId = index.TipId,
                Nodes = index.Nodes,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MafSessionSnapshot?> TryCheckoutAsync(
        string workingDirectory,
        string nodeIdOrPrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeIdOrPrefix);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await ReadIndexAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            var match = ResolveNodeId(index.Nodes, nodeIdOrPrefix);
            if (match is null || !index.Sessions.TryGetValue(match, out var session))
            {
                return null;
            }

            var path = index.FilePath;
            var at = DateTimeOffset.UtcNow;
            await using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                var tip = new { t = "tip", at, nodeId = match };
                await writer.WriteLineAsync(JsonSerializer.Serialize(tip, JsonOptions)).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            _tipId = match;
            _indexedPath = path;

            return new MafSessionSnapshot
            {
                SessionId = index.SessionId ?? "unknown",
                WorkingDirectory = SessionPath.NormalizeWorkingDirectory(workingDirectory),
                Session = session,
                FilePath = path,
                NodeId = match,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<FileIndex> ReadIndexAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var path = GetSessionFilePath(workingDirectory);
        return await ReadIndexUnlockedAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FileIndex> ReadIndexUnlockedAsync(string path, CancellationToken cancellationToken)
    {
        var nodes = new List<SessionNode>();
        var sessions = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        string? sessionId = null;
        string? tipId = null;
        string? lastNodeId = null;
        var legacyOrdinal = 0;

        if (!File.Exists(path))
        {
            return new FileIndex(path, sessionId, tipId, nodes, sessions);
        }

        await foreach (var line in File.ReadLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Truncated / corrupt trailing line (crash mid-write) — keep prior complete nodes.
                break;
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("t", out var typeEl))
                {
                    continue;
                }

                var type = typeEl.GetString();
                if (type is "meta" && root.TryGetProperty("sessionId", out var idEl))
                {
                    sessionId = idEl.GetString();
                    continue;
                }

                if (type is "tip" && root.TryGetProperty("nodeId", out var tipEl))
                {
                    tipId = tipEl.GetString();
                    continue;
                }

                if (type is "turn")
                {
                    var nodeId = root.TryGetProperty("nodeId", out var nid) ? nid.GetString() : null;
                    var parentId = root.TryGetProperty("parentId", out var pid) && pid.ValueKind != JsonValueKind.Null
                        ? pid.GetString()
                        : null;
                    var user = root.TryGetProperty("user", out var u) ? u.GetString() ?? "" : "";
                    var assistant = root.TryGetProperty("assistant", out var a) ? a.GetString() : null;
                    var at = root.TryGetProperty("at", out var atEl) && atEl.TryGetDateTimeOffset(out var dto)
                        ? dto
                        : DateTimeOffset.UtcNow;

                    if (string.IsNullOrWhiteSpace(nodeId))
                    {
                        legacyOrdinal++;
                        nodeId = $"legacy{legacyOrdinal:D4}";
                        parentId = lastNodeId;
                    }

                    // Prefer turn for previews; maf may arrive without re-adding.
                    var existing = nodes.FindIndex(n => string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase));
                    var node = new SessionNode
                    {
                        Id = nodeId!,
                        ParentId = parentId,
                        UserPreview = Truncate(user, 60),
                        AssistantPreview = assistant is null ? null : Truncate(assistant, 40),
                        At = at,
                    };
                    if (existing >= 0)
                    {
                        nodes[existing] = node;
                    }
                    else
                    {
                        nodes.Add(node);
                    }

                    lastNodeId = nodeId;
                    continue;
                }

                if (type is "maf" && root.TryGetProperty("session", out var sessionEl))
                {
                    if (root.TryGetProperty("sessionId", out var sid))
                    {
                        sessionId = sid.GetString() ?? sessionId;
                    }

                    var nodeId = root.TryGetProperty("nodeId", out var nid) ? nid.GetString() : null;
                    var parentId = root.TryGetProperty("parentId", out var pid) && pid.ValueKind != JsonValueKind.Null
                        ? pid.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(nodeId))
                    {
                        // Legacy: attach to last turn node or synthesize.
                        if (lastNodeId is not null && sessions.ContainsKey(lastNodeId) == false)
                        {
                            nodeId = lastNodeId;
                        }
                        else
                        {
                            legacyOrdinal++;
                            nodeId = $"legacy{legacyOrdinal:D4}";
                            parentId ??= lastNodeId;
                            if (nodes.All(n => !string.Equals(n.Id, nodeId, StringComparison.OrdinalIgnoreCase)))
                            {
                                nodes.Add(new SessionNode
                                {
                                    Id = nodeId,
                                    ParentId = parentId,
                                    UserPreview = "(legacy)",
                                    At = DateTimeOffset.UtcNow,
                                });
                            }
                        }
                    }

                    sessions[nodeId!] = sessionEl.Clone();
                    lastNodeId = nodeId;
                    tipId ??= nodeId; // until explicit tip line
                }
            }
        }

        // Prefer explicit tip; else last node with a session.
        if (tipId is null || !sessions.ContainsKey(tipId))
        {
            tipId = nodes.AsEnumerable().Reverse().Select(n => n.Id).FirstOrDefault(id => sessions.ContainsKey(id))
                ?? lastNodeId;
        }

        return new FileIndex(path, sessionId, tipId, nodes, sessions);
    }

    private static string? ResolveNodeId(IReadOnlyList<SessionNode> nodes, string idOrPrefix)
    {
        var exact = nodes.FirstOrDefault(n => string.Equals(n.Id, idOrPrefix, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Id;
        }

        var matches = nodes
            .Where(n => n.Id.StartsWith(idOrPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static string Truncate(string value, int max)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (oneLine.Length <= max)
        {
            return oneLine;
        }

        return oneLine[..max] + "…";
    }

    private sealed class FileIndex(
        string filePath,
        string? sessionId,
        string? tipId,
        List<SessionNode> nodes,
        Dictionary<string, JsonElement> sessions)
    {
        public string FilePath { get; } = filePath;

        public string? SessionId { get; } = sessionId;

        public string? TipId { get; } = tipId;

        public List<SessionNode> Nodes { get; } = nodes;

        public Dictionary<string, JsonElement> Sessions { get; } = sessions;
    }
}
