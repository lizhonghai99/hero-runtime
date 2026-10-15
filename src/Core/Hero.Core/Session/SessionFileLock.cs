using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Hero.Core.Session;

/// <summary>
/// Cross-process exclusive lock for a session JSONL path via a sibling <c>.lock</c> file.
/// </summary>
internal static class SessionFileLock
{
    private const int PollMs = 50;

    public static async Task<IAsyncDisposable> AcquireAsync(string sessionFilePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);

        var dir = Path.GetDirectoryName(sessionFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lockPath = sessionFilePath + ".lock";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // DeleteOnClose so a crashed process does not leave a permanent lock file.
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
                return new Releaser(stream);
            }
            catch (IOException)
            {
                await Task.Delay(PollMs, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(PollMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Releaser(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
