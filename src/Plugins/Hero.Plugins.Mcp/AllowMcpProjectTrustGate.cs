using System.Threading;
using System.Threading.Tasks;

namespace Hero.Plugins.Mcp;

/// <summary>Always allow project mcp.json (tests / explicit host opt-in after fingerprint persist).</summary>
public sealed class AllowMcpProjectTrustGate : IMcpProjectTrustGate
{
    public Task<bool> AllowProjectMcpAsync(
        string projectConfigPath,
        string contentHash,
        int serverCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
