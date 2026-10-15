using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Options;
using Hero.Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hero.Core.Observability;

/// <summary>
/// Appends one JSON object per turn under ~/.hero-agent/logs/&lt;cwd-key&gt;/turns.jsonl.
/// </summary>
internal sealed class JsonlTurnLogger : ITurnLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HeroOptions _options;
    private readonly ILogger<JsonlTurnLogger> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonlTurnLogger(IOptions<HeroOptions> options, ILogger<JsonlTurnLogger> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string GetLogFilePath() => GetLogFilePath(_options.WorkingDirectory);

    public string GetLogFilePath(string? workingDirectory)
    {
        var cwd = SessionPath.NormalizeWorkingDirectory(workingDirectory ?? _options.WorkingDirectory);
        var key = SessionPath.GetDirectoryKey(cwd);
        return Path.Combine(_options.TurnLogDirectory, key, "turns.jsonl");
    }


    public async Task LogAsync(TurnRecord turn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cwd = turn.WorkingDirectory ?? _options.WorkingDirectory;
            var path = GetLogFilePath(cwd);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var payload = new
            {
                turnId = turn.TurnId,
                sessionId = turn.SessionId,
                cwd = SessionPath.NormalizeWorkingDirectory(cwd),
                status = turn.Status,
                startedAt = turn.StartedAt,
                endedAt = turn.EndedAt,
                durationMs = turn.DurationMs,
                user = turn.UserInput,
                assistant = turn.AssistantOutput,
                tools = turn.ToolEvents,
                error = turn.Error,
                inputTokens = turn.InputTokens,
                outputTokens = turn.OutputTokens,
                totalTokens = turn.TotalTokens,
                cachedInputTokens = turn.CachedInputTokens,
                estimatedCostUsd = turn.EstimatedCostUsd,
            };

            var line = JsonSerializer.Serialize(payload, JsonOptions);

            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Turn {TurnId} logged to {Path} ({Status}, {DurationMs} ms)",
                turn.TurnId, path, turn.Status, turn.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write turn log for {TurnId}", turn.TurnId);
        }
        finally
        {
            _gate.Release();
        }
    }
}
