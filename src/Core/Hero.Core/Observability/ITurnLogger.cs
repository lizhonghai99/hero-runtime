using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Observability;

public interface ITurnLogger
{
    Task LogAsync(TurnRecord turn, CancellationToken cancellationToken = default);

    /// <summary>Active turn log file for the current working directory, if file-backed.</summary>
    string? GetLogFilePath();
}

