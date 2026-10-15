using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Hosting;
using Hero.Core.Providers;

namespace Hero.Cli.Tui;

/// <summary>Terminal.Gui /model flow: provider → key → model list → apply.</summary>
internal sealed class TuiModelPicker
{
    private readonly AgentHost _host;
    private readonly TuiDialogService _dialogs;
    private readonly Action<string> _log;

    public TuiModelPicker(AgentHost host, TuiDialogService dialogs, Action<string> log)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task RunAsync(CancellationToken cancellationToken, string? preferredProviderId = null)
    {
        var chat = _host.Chat;
        _log($"current> {(string.IsNullOrWhiteSpace(chat.ProviderId) ? "?" : chat.ProviderId)} · {chat.Model}");

        LlmProviderDefinition? def = LlmProviderCatalog.Find(preferredProviderId);
        if (def is null)
        {
            var providers = LlmProviderCatalog.All;
            var labels = new List<string>(providers.Count);
            var selected = 0;
            for (var i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                var hasKey = !string.IsNullOrWhiteSpace(
                    await _host.ResolveApiKeyAsync(p.Id, cancellationToken).ConfigureAwait(false));
                var mark = string.Equals(p.Id, chat.ProviderId, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                labels.Add($"{mark} {i + 1}. {p.DisplayName}  ({p.Id})  [{(hasKey ? "key ok" : "need key")}]");
                if (string.Equals(p.Id, chat.ProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                }
            }

            var pick = await _dialogs
                .PickIndexAsync("Provider", "Select a provider (Esc / Cancel to abort)", labels, cancellationToken, selected)
                .ConfigureAwait(false);
            if (pick is null)
            {
                _log("model> cancelled");
                return;
            }

            def = providers[pick.Value];
        }

        _log($"provider> {def.DisplayName} ({def.Id})");

        var apiKey = await _host.ResolveApiKeyAsync(def.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await _dialogs
                .PromptAsync("API key", $"API key for {def.DisplayName} (memory only, not saved)", cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _log("model> cancelled — no API key");
                return;
            }
        }
        else
        {
            var use = await _dialogs
                .QueryAsync(
                    "API key",
                    $"Use existing env/session key for {def.DisplayName}?",
                    cancellationToken,
                    "No",
                    "Yes")
                .ConfigureAwait(false);
            if (use != 1)
            {
                apiKey = await _dialogs
                    .PromptAsync("API key", $"API key for {def.DisplayName} (memory only)", cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _log("model> cancelled — no API key");
                    return;
                }
            }
        }

        _log($"fetching models from {def.ModelsListUrl} …");
        IReadOnlyList<RemoteModelInfo> models;
        try
        {
            models = await _host.ListRemoteModelsAsync(def.Id, apiKey!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log("error> " + ex.Message);
            return;
        }

        if (models.Count == 0)
        {
            _log("model> no models returned");
            return;
        }

        var modelLabels = new List<string>(models.Count);
        var modelSelected = 0;
        for (var i = 0; i < models.Count; i++)
        {
            var m = models[i];
            var mark = string.Equals(m.Id, chat.Model, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(def.Id, chat.ProviderId, StringComparison.OrdinalIgnoreCase)
                ? "*"
                : " ";
            modelLabels.Add($"{mark} {i + 1}. {m.Display}");
            if (string.Equals(m.Id, chat.Model, StringComparison.OrdinalIgnoreCase))
            {
                modelSelected = i;
            }
        }

        var modelPick = await _dialogs
            .PickIndexAsync("Model", "Select a model (Esc / Cancel to abort)", modelLabels, cancellationToken, modelSelected)
            .ConfigureAwait(false);
        if (modelPick is null)
        {
            _log("model> cancelled");
            return;
        }

        var modelId = models[modelPick.Value].Id;
        await _host.ApplyProviderSelectionAsync(def.Id, apiKey!, modelId, cancellationToken).ConfigureAwait(false);
        _log($"chat> {def.DisplayName} · {modelId}");
        _log($"settings → {_host.SettingsStore.FilePath}");
    }
}
