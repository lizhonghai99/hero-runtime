using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hero.Core.Options;
using Hero.Core.Session;
using Microsoft.Extensions.Options;

namespace Hero.Core.Observability;

/// <summary>
/// In-process store of model calls, bucketed by turn, with optional JSONL persistence
/// under ~/.hero-agent/logs/&lt;cwd-key&gt;/traces.jsonl.
/// Disk writes are serialized via a single-consumer channel (safe under concurrent sub-agents).
/// </summary>
public sealed class ChatTraceStore : IAsyncDisposable, IDisposable
{
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly int _maxTurns;
    private readonly HeroOptions? _options;
    private readonly List<TurnTrace> _turns = [];
    private readonly Channel<PersistWork>? _persistQueue;
    private readonly Task? _pumpTask;
    private string? _activeTurnId;
    private string? _expectedUserInput;
    private int _disposed;

    public ChatTraceStore(int maxTurns = 50)
        : this((HeroOptions?)null, maxTurns)
    {
    }

    public ChatTraceStore(HeroOptions? options, int maxTurns = 50)
    {
        _options = options;
        _maxTurns = Math.Max(1, maxTurns);
        if (options is not null)
        {
            _persistQueue = Channel.CreateUnbounded<PersistWork>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                });
            _pumpTask = PumpPersistAsync(_persistQueue.Reader);
        }
    }

    public static ChatTraceStore Create(IOptions<HeroOptions> options, int maxTurns = 50) =>
        new(options?.Value, maxTurns);

    /// <summary>Turn currently being executed (null between turns).</summary>
    public string? ActiveTurnId
    {
        get
        {
            lock (_gate)
            {
                return _activeTurnId;
            }
        }
    }

    /// <summary>
    /// User text for the active turn. Used by <see cref="TurnBoundChatClient"/> to detect
    /// MAF/Harness paths that replay a stale first user message.
    /// </summary>
    public string? ExpectedUserInput
    {
        get
        {
            lock (_gate)
            {
                return _expectedUserInput;
            }
        }
    }

    public string GetTracesFilePath(string? workingDirectory = null)
    {
        var opts = _options ?? HeroOptions.CreateDefault();
        var cwd = SessionPath.NormalizeWorkingDirectory(workingDirectory ?? opts.WorkingDirectory);
        var key = SessionPath.GetDirectoryKey(cwd);
        return Path.Combine(opts.TurnLogDirectory, key, "traces.jsonl");
    }

    public void BeginTurn(string turnId, string? expectedUserInput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(turnId);
        lock (_gate)
        {
            _activeTurnId = turnId;
            _expectedUserInput = expectedUserInput;
            _turns.Add(new TurnTrace(turnId));
            while (_turns.Count > _maxTurns)
            {
                _turns.RemoveAt(0);
            }
        }
    }

    public void EndTurn(string? turnId = null)
    {
        lock (_gate)
        {
            if (turnId is null || string.Equals(_activeTurnId, turnId, StringComparison.Ordinal))
            {
                _activeTurnId = null;
                _expectedUserInput = null;
            }
        }
    }

    public void AddCall(ModelCallRecord call)
    {
        ArgumentNullException.ThrowIfNull(call);
        string? line = null;
        lock (_gate)
        {
            var turn = FindTurn(call.TurnId) ?? (_turns.Count > 0 ? _turns[^1] : null);
            if (turn is null)
            {
                turn = new TurnTrace(call.TurnId);
                _turns.Add(turn);
                while (_turns.Count > _maxTurns)
                {
                    _turns.RemoveAt(0);
                }
            }

            call.CallIndex = turn.Calls.Count + 1;
            turn.Calls.Add(call);
            line = SerializeCallLine(call);
        }

        // Enqueue outside the memory lock; single consumer serializes AppendAllTextAsync.
        if (line is not null)
        {
            EnqueuePersist(line);
        }
    }

    /// <summary>Wait until all previously enqueued trace lines have been written (or failed soft).</summary>
    public async Task FlushPersistAsync(CancellationToken cancellationToken = default)
    {
        if (_persistQueue is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_persistQueue.Writer.TryWrite(PersistWork.CreateFlush(done)))
        {
            return;
        }

        using var reg = cancellationToken.Register(
            static state =>
            {
                var (tcs, ct) = ((TaskCompletionSource, CancellationToken))state!;
                tcs.TrySetCanceled(ct);
            },
            (done, cancellationToken));
        await done.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _persistQueue?.Writer.TryComplete();
        if (_pumpTask is null)
        {
            return;
        }

        try
        {
            var finished = await Task
                .WhenAny(_pumpTask, Task.Delay(DisposeDrainTimeout))
                .ConfigureAwait(false);
            if (finished == _pumpTask)
            {
                await _pumpTask.ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Soft-fail dispose: never throw from shutdown flush.
        }
    }

    public IReadOnlyList<TurnTrace> SnapshotTurns()
    {
        lock (_gate)
        {
            return _turns.Select(t => t.Clone()).ToList();
        }
    }

    /// <summary>
    /// Load persisted traces from disk (grouped by turn). Falls back to in-memory snapshot when empty.
    /// </summary>
    public IReadOnlyList<TurnTrace> LoadPersistedTurns(string? workingDirectory = null)
    {
        var path = GetTracesFilePath(workingDirectory);
        if (!File.Exists(path))
        {
            return SnapshotTurns();
        }

        try
        {
            var byTurn = new Dictionary<string, TurnTrace>(StringComparer.Ordinal);
            var order = new List<string>();

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var call = JsonSerializer.Deserialize<ModelCallRecord>(line, JsonOptions);
                if (call is null || string.IsNullOrWhiteSpace(call.TurnId))
                {
                    continue;
                }

                if (!byTurn.TryGetValue(call.TurnId, out var turn))
                {
                    turn = new TurnTrace(call.TurnId);
                    byTurn[call.TurnId] = turn;
                    order.Add(call.TurnId);
                }

                call.CallIndex = turn.Calls.Count + 1;
                turn.Calls.Add(call);
            }

            if (order.Count == 0)
            {
                return SnapshotTurns();
            }

            return order.Select(id => byTurn[id].Clone()).ToList();
        }
        catch (IOException)
        {
            return SnapshotTurns();
        }
        catch (UnauthorizedAccessException)
        {
            return SnapshotTurns();
        }
        catch (JsonException)
        {
            return SnapshotTurns();
        }
    }

    /// <summary>1 = latest turn, 2 = previous, …</summary>
    public TurnTrace? GetTurnFromEnd(int fromEnd)
    {
        if (fromEnd < 1)
        {
            return null;
        }

        lock (_gate)
        {
            var index = _turns.Count - fromEnd;
            return index >= 0 ? _turns[index].Clone() : null;
        }
    }

    public TurnTrace? GetLatestTurn() => GetTurnFromEnd(1);

    private TurnTrace? FindTurn(string turnId)
    {
        for (var i = _turns.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_turns[i].TurnId, turnId, StringComparison.Ordinal))
            {
                return _turns[i];
            }
        }

        return null;
    }

    private string? SerializeCallLine(ModelCallRecord call)
    {
        if (_options is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(call, JsonOptions);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void EnqueuePersist(string line)
    {
        _persistQueue?.Writer.TryWrite(PersistWork.CreateLine(line));
    }

    private async Task PumpPersistAsync(ChannelReader<PersistWork> reader)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await foreach (var work in reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (work.Flush is not null)
            {
                work.Flush.TrySetResult();
                continue;
            }

            if (string.IsNullOrEmpty(work.JsonLine))
            {
                continue;
            }

            try
            {
                var path = GetTracesFilePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.AppendAllTextAsync(path, work.JsonLine + Environment.NewLine, utf8)
                    .ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private readonly struct PersistWork
    {
        public string? JsonLine { get; private init; }

        public TaskCompletionSource? Flush { get; private init; }

        public static PersistWork CreateLine(string jsonLine) => new() { JsonLine = jsonLine };

        public static PersistWork CreateFlush(TaskCompletionSource signal) => new() { Flush = signal };
    }
}

public sealed class TurnTrace
{
    public TurnTrace(string turnId)
    {
        TurnId = turnId;
        Calls = [];
    }

    public string TurnId { get; }

    public List<ModelCallRecord> Calls { get; }

    public TurnTrace Clone()
    {
        var copy = new TurnTrace(TurnId);
        copy.Calls.AddRange(Calls.Select(c => c.Clone()));
        return copy;
    }
}

public sealed class ModelCallRecord
{
    public required string TurnId { get; init; }

    /// <summary>Main agent name (e.g. hi) or sub-agent name (e.g. ExploreAgent).</summary>
    public string? AgentName { get; init; }

    public int CallIndex { get; set; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    public double? DurationMs { get; set; }

    public bool Streaming { get; init; }

    public List<MessageSummary> Messages { get; init; } = [];

    public List<string> ToolNames { get; init; } = [];

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedInputTokens { get; set; }

    public string? Error { get; set; }

    public ModelCallRecord Clone() => new()
    {
        TurnId = TurnId,
        AgentName = AgentName,
        CallIndex = CallIndex,
        StartedAt = StartedAt,
        EndedAt = EndedAt,
        DurationMs = DurationMs,
        Streaming = Streaming,
        Messages = Messages.Select(m => new MessageSummary { Role = m.Role, Text = m.Text, CharCount = m.CharCount }).ToList(),
        ToolNames = [.. ToolNames],
        InputTokens = InputTokens,
        OutputTokens = OutputTokens,
        CachedInputTokens = CachedInputTokens,
        Error = Error,
    };
}

public sealed class MessageSummary
{
    public required string Role { get; init; }

    public required string Text { get; init; }

    public int CharCount { get; init; }
}
