using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Hero.Cli.Tui;

/// <summary>
/// Marshals dialogs onto the Terminal.Gui UI thread.
/// Approval / MCP trust / prompts can call from agent background tasks safely.
/// </summary>
internal sealed class TuiDialogService
{
    private IApplication? _app;

    public void Attach(IApplication app) =>
        _app = app ?? throw new ArgumentNullException(nameof(app));

    public Task<int> QueryAsync(
        string title,
        string message,
        CancellationToken cancellationToken,
        params string[] buttons)
    {
        if (buttons.Length == 0)
        {
            throw new ArgumentException("At least one button is required.", nameof(buttons));
        }

        var app = _app;
        if (app is null)
        {
            return Task.FromResult(buttons.Length - 1);
        }

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        app.Invoke(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var result = MessageBox.Query(app, title, message, buttons);
                tcs.TrySetResult(result ?? buttons.Length - 1);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>Modal text prompt. Returns null if cancelled.</summary>
    public Task<string?> PromptAsync(
        string title,
        string message,
        CancellationToken cancellationToken,
        string? initial = null)
    {
        var app = _app;
        if (app is null)
        {
            return Task.FromResult<string?>(null);
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        app.Invoke(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var dialog = new Dialog
                {
                    Title = title,
                    Width = 56,
                    Height = 9,
                };

                var label = new Label
                {
                    Text = message,
                    X = 1,
                    Y = 0,
                    Width = Dim.Fill(1),
                    Height = 2,
                };

                var field = new TextField
                {
                    Text = initial ?? string.Empty,
                    X = 1,
                    Y = 2,
                    Width = Dim.Fill(1),
                };

                dialog.Add(label, field);

                var cancel = new Button { Text = "Cancel" };
                var ok = new Button { Text = "OK" };

                void Finish(string? value)
                {
                    tcs.TrySetResult(value);
                    app.RequestStop(dialog);
                }

                cancel.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(null);
                };
                ok.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(field.Text?.ToString());
                };
                field.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(field.Text?.ToString());
                };

                dialog.AddButton(cancel);
                dialog.AddButton(ok);

                field.SetFocus();
                app.Run(dialog);
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Modal list picker. Returns selected index, or null if cancelled.
    /// </summary>
    public Task<int?> PickIndexAsync(
        string title,
        string message,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken,
        int selectedIndex = 0)
    {
        if (items.Count == 0)
        {
            return Task.FromResult<int?>(null);
        }

        var app = _app;
        if (app is null)
        {
            return Task.FromResult<int?>(Math.Clamp(selectedIndex, 0, items.Count - 1));
        }

        var tcs = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        app.Invoke(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                // Explicit list height — Dim.Fill inside Dialog often collapses to 1 row
                // after the button bar steals bottom padding.
                var listHeight = Math.Clamp(items.Count, 2, 14);
                var dialog = new Dialog
                {
                    Title = title,
                    Width = Math.Min(78, Math.Max(48, LongestLine(items) + 10)),
                    Height = listHeight + 8,
                };

                var label = new Label
                {
                    Text = message,
                    X = 1,
                    Y = 0,
                    Width = Dim.Fill(1),
                    Height = 1,
                };

                var collection = new ObservableCollection<string>(items);
                var list = new ListView
                {
                    X = 1,
                    Y = 2,
                    Width = Dim.Fill(1),
                    Height = listHeight,
                    CanFocus = true,
                };
                list.SetSource(collection);

                var initial = Math.Clamp(selectedIndex, 0, items.Count - 1);
                list.SelectedItem = initial;
                list.EnsureSelectedItemVisible();

                dialog.Add(label, list);

                var cancel = new Button { Text = "Cancel" };
                var ok = new Button { Text = "OK" };

                void Finish(int? value)
                {
                    tcs.TrySetResult(value);
                    app.RequestStop(dialog);
                }

                cancel.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(null);
                };
                ok.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(list.SelectedItem);
                };
                list.Accepting += (_, e) =>
                {
                    e.Handled = true;
                    Finish(list.SelectedItem);
                };

                dialog.AddButton(cancel);
                dialog.AddButton(ok);

                list.SetFocus();
                app.Run(dialog);
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static int LongestLine(IReadOnlyList<string> items)
    {
        var max = 0;
        foreach (var item in items)
        {
            if (item.Length > max)
            {
                max = item.Length;
            }
        }

        return max;
    }
}
