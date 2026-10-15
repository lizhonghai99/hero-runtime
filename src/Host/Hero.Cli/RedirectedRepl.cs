using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Agent;
using Hero.Core.Hosting;
using Hero.Core.Plugins;
using Hero.Core.Providers;

namespace Hero.Cli;

/// <summary>
/// Minimal stdin/stdout REPL for redirected pipes. Interactive users get Terminal.Gui.
/// </summary>
internal sealed class RedirectedRepl
{
    private readonly AgentHost _host;
    private readonly PluginCatalog _catalog;
    private readonly ConsoleCancelScope _cancel;
    private readonly BuiltinSlashCommands _slash;

    public RedirectedRepl(AgentHost host, PluginCatalog catalog, ConsoleCancelScope cancel)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        _slash = new BuiltinSlashCommands(host);
    }

    public async Task RunAsync()
    {
        Console.WriteLine("hi redirected REPL — /help · /exit");
        var slashCtx = new ConsoleSlashContext(_host);

        while (!_cancel.AppToken.IsCancellationRequested)
        {
            Console.Write("you> ");
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync().WaitAsync(_cancel.AppToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Equals("/exit", StringComparison.OrdinalIgnoreCase)
                || line.Equals("/quit", StringComparison.OrdinalIgnoreCase)
                || line.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (line.StartsWith('!'))
            {
                await RunBangAsync(line).ConfigureAwait(false);
                continue;
            }

            if (line.StartsWith('/'))
            {
                if (await TrySlashAsync(line, slashCtx).ConfigureAwait(false))
                {
                    continue;
                }

                Console.WriteLine("unknown> " + line);
                continue;
            }

            if (!_host.IsChatConfigured)
            {
                ConsoleRenderer.Current.WriteError("chat not configured — set API key or /model");
                continue;
            }

            try
            {
                var turnCts = _cancel.BeginTurn();
                await foreach (var chunk in _host.RunTurnStreamingAsync(line, turnCts.Token).ConfigureAwait(false))
                {
                    if (chunk.Kind == AgentStreamKind.Text && !string.IsNullOrEmpty(chunk.Text))
                    {
                        Console.Write(chunk.Text);
                    }
                    else if (chunk.Kind == AgentStreamKind.Tool)
                    {
                        Console.Error.WriteLine("tool> " + chunk.Text);
                    }
                }

                Console.WriteLine();
            }
            catch (OperationCanceledException) when (!_cancel.AppToken.IsCancellationRequested)
            {
                Console.WriteLine("cancelled>");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsoleRenderer.Current.WriteError(ex.Message);
            }
            finally
            {
                _cancel.EndTurn();
            }
        }
    }

    private async Task<bool> TrySlashAsync(string line, ISlashCommandContext slashCtx)
    {
        try
        {
            var result = await _slash
                .TryHandleAsync(line, _catalog.SlashCommands, _cancel.AppToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                if (result.Action == BuiltinSlashAction.OpenModelPicker)
                {
                    await SimpleModelPickAsync(result.PreferredProviderId).ConfigureAwait(false);
                }
                else
                {
                    foreach (var row in result.Lines)
                    {
                        Console.WriteLine("  " + row);
                    }
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Current.WriteError(ex.Message);
            return true;
        }

        foreach (var cmd in _catalog.SlashCommands)
        {
            if (line.Equals("/" + cmd.Name, StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("/" + cmd.Name + " ", StringComparison.OrdinalIgnoreCase))
            {
                return await cmd.TryHandleAsync(line, slashCtx, _cancel.AppToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task SimpleModelPickAsync(string? preferredProviderId)
    {
        var def = LlmProviderCatalog.Find(preferredProviderId) ?? LlmProviderCatalog.All[0];
        Console.WriteLine($"provider> {def.DisplayName} ({def.Id})");
        var key = await _host.ResolveApiKeyAsync(def.Id, _cancel.AppToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Write("api key> ");
            key = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(key))
            {
                Console.WriteLine("cancelled>");
                return;
            }
        }

        var models = await _host.ListRemoteModelsAsync(def.Id, key!, _cancel.AppToken).ConfigureAwait(false);
        for (var i = 0; i < models.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {models[i].Display}");
        }

        Console.Write("model # or id> ");
        var choice = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(choice))
        {
            Console.WriteLine("cancelled>");
            return;
        }

        string modelId;
        if (int.TryParse(choice, out var idx) && idx >= 1 && idx <= models.Count)
        {
            modelId = models[idx - 1].Id;
        }
        else
        {
            modelId = models.FirstOrDefault(m => m.Id.Equals(choice, StringComparison.OrdinalIgnoreCase))?.Id
                      ?? choice;
        }

        await _host.ApplyProviderSelectionAsync(def.Id, key!, modelId, _cancel.AppToken).ConfigureAwait(false);
        Console.WriteLine($"chat> {def.DisplayName} · {modelId}");
    }

    private async Task RunBangAsync(string trimmed)
    {
        var blind = trimmed.StartsWith("!!", StringComparison.Ordinal);
        var command = (blind ? trimmed[2..] : trimmed[1..]).Trim();
        if (command.Length == 0)
        {
            Console.WriteLine("usage> !cmd | !!cmd");
            return;
        }

        try
        {
            var (code, output) = await LocalCommandRunner.RunAsync(command, _cancel.AppToken).ConfigureAwait(false);
            Console.WriteLine(string.IsNullOrEmpty(output) ? "(no output)" : output);
            Console.WriteLine("exit> " + code);
            if (!blind && _host.IsChatConfigured)
            {
                var payload =
                    $"""
                    I ran this shell command and want you to use the result:

                    ```
                    {command}
                    ```

                    Exit code: {code}

                    Output:
                    ```
                    {Truncate(output, 12_000)}
                    ```
                    """;
                await foreach (var chunk in _host.RunTurnStreamingAsync(payload, _cancel.AppToken).ConfigureAwait(false))
                {
                    if (chunk.Kind == AgentStreamKind.Text)
                    {
                        Console.Write(chunk.Text);
                    }
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            ConsoleRenderer.Current.WriteError(ex.Message);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";

    private sealed class ConsoleSlashContext(AgentHost host) : ISlashCommandContext
    {
        public IAgentRuntime Runtime { get; } = new AgentHostRuntimeAdapter(host);

        public void WriteLine(string text) => Console.WriteLine(text);

        public void WriteError(string text) => ConsoleRenderer.Current.WriteError(text);

        public void WriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) =>
            ConsoleRenderer.Current.WriteTable(headers, rows);
    }
}
