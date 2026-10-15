using System.Threading;
using System.Threading.Tasks;

namespace Hero.Plugins.Mcp;

/// <summary>
/// Decides whether project-level <c>.hero-agent/mcp.json</c> may spawn local processes.
/// </summary>
public interface IMcpProjectTrustGate
{
    /// <summary>
    /// Evaluate trust for the project config. May prompt interactively and persist acceptance.
    /// </summary>
    Task<bool> AllowProjectMcpAsync(
        string projectConfigPath,
        string contentHash,
        int serverCount,
        CancellationToken cancellationToken = default);
}
