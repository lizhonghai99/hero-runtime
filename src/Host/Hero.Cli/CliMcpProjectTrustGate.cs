using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Mcp;

namespace Hero.Cli;

/// <summary>
/// Interactive / flag-driven trust for project <c>mcp.json</c>.
/// Non-interactive hosts deny unless force-trust (e.g. <c>--trust-project-mcp</c>).
/// </summary>
internal sealed class CliMcpProjectTrustGate : IMcpProjectTrustGate
{
    private readonly McpProjectTrustStore _store;
    private readonly bool _forceTrust;
    private readonly bool _allowPrompt;
    private readonly TurnInputBroker _input;

    public CliMcpProjectTrustGate(
        bool forceTrust,
        bool allowPrompt,
        TurnInputBroker input,
        McpProjectTrustStore? store = null)
    {
        _forceTrust = forceTrust;
        _allowPrompt = allowPrompt;
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _store = store ?? new McpProjectTrustStore();
    }

    public async Task<bool> AllowProjectMcpAsync(
        string projectConfigPath,
        string contentHash,
        int serverCount,
        CancellationToken cancellationToken = default)
    {
        if (_store.IsTrusted(projectConfigPath, contentHash))
        {
            return true;
        }

        if (_forceTrust)
        {
            _store.Trust(projectConfigPath, contentHash);
            return true;
        }

        if (!_allowPrompt || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(
            $"This project's .hero-agent/mcp.json will start {serverCount} local process(es). Trust it? [y/N]");
        Console.ResetColor();
        Console.Write("> ");

        string? line;
        try
        {
            line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        var yes = !string.IsNullOrWhiteSpace(line)
            && line.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
        if (yes)
        {
            _store.Trust(projectConfigPath, contentHash);
        }

        return yes;
    }
}
