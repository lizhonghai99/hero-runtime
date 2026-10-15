using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hero.Core.Plugins;

namespace Hero.Plugins.Harness;

internal sealed class TodosSlashCommand : ISlashCommand
{
    public string Name => "todos";

    public string Description => "/todos — list todo items";

    public async Task<bool> TryHandleAsync(
        string line,
        ISlashCommandContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!line.Equals("/todos", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var todos = await context.Runtime.GetTodosAsync(cancellationToken).ConfigureAwait(false);
            if (todos.Count == 0)
            {
                context.WriteLine("  (no todos yet)");
            }
            else
            {
                var rows = new List<IReadOnlyList<string>>(todos.Count);
                foreach (var item in todos)
                {
                    var title = item.Title;
                    if (!string.IsNullOrWhiteSpace(item.Description))
                    {
                        title += $" — {item.Description}";
                    }

                    rows.Add(
                    [
                        item.Id,
                        title,
                        item.IsComplete ? "done" : "open",
                    ]);
                }

                context.WriteTable(["id", "title", "status"], rows);
            }
        }
        catch (Exception ex)
        {
            context.WriteError(ex.Message);
        }

        return true;
    }
}
