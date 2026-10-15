using System.Threading;
using System.Threading.Tasks;

namespace Hero.Plugins.Mcp;

/// <summary>Fail-closed: never trust project-level mcp.json (non-CLI hosts / default DI).</summary>
public sealed class DenyMcpProjectTrustGate : IMcpProjectTrustGate
{
    public Task<bool> AllowProjectMcpAsync(
        string projectConfigPath,
        string contentHash,
        int serverCount,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
