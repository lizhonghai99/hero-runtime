using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Plugins.Mcp;

namespace Hero.Cli.Tui;

/// <summary>Project <c>mcp.json</c> trust prompt via Terminal.Gui dialog.</summary>
internal sealed class TuiMcpProjectTrustGate : IMcpProjectTrustGate
{
    private readonly McpProjectTrustStore _store;
    private readonly bool _forceTrust;
    private readonly bool _allowPrompt;
    private readonly TuiDialogService _dialogs;

    public TuiMcpProjectTrustGate(
        bool forceTrust,
        bool allowPrompt,
        TuiDialogService dialogs,
        McpProjectTrustStore? store = null)
    {
        _forceTrust = forceTrust;
        _allowPrompt = allowPrompt;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
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

        if (!_allowPrompt)
        {
            return false;
        }

        int index;
        try
        {
            index = await _dialogs
                .QueryAsync(
                    "MCP project trust",
                    $"This project's .hero-agent/mcp.json will start {serverCount} local process(es). Trust it?",
                    cancellationToken,
                    "No",
                    "Yes")
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        var yes = index == 1;
        if (yes)
        {
            _store.Trust(projectConfigPath, contentHash);
        }

        return yes;
    }
}
