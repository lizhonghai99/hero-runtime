using System;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Hosting;
using Hero.Core.Plugins;
using Terminal.Gui.App;

namespace Hero.Cli.Tui;

/// <summary>Runs the interactive Terminal.Gui shell (blocks until quit).</summary>
internal static class TuiReplHost
{
    public static Task RunAsync(
        AgentHost host,
        PluginCatalog catalog,
        ConsoleCancelScope cancel,
        TuiDialogService dialogs,
        string startupBanner,
        CancellationToken cancellationToken)
    {
        // Application.Run is synchronous; hop off the caller if needed.
        return Task.Run(
            () =>
            {
                // v2: Create().Init() before any Run / before constructing views that touch drivers.
                using var app = Application.Create().Init();
                TuiTheme.Register();
                using var window = new MainWindow(host, catalog, cancel, dialogs, startupBanner);

                using var reg = cancellationToken.Register(() =>
                {
                    try
                    {
                        app.Invoke(() => app.RequestStop());
                    }
                    catch
                    {
                        // App may already be disposing.
                    }
                });

                app.Run(window);
            },
            CancellationToken.None);
    }
}
