using System;
using System.Collections.Generic;
using System.Linq;

namespace Hero.Core.Session;

/// <summary>Host-agnostic tip-tree rendering (plain lines).</summary>
public static class SessionTreeFormatter
{
    public static IReadOnlyList<string> FormatLines(SessionTreeSnapshot tree, string indentUnit = "  ")
    {
        ArgumentNullException.ThrowIfNull(tree);

        var lines = new List<string>();
        var byParent = tree.Nodes
            .GroupBy(n => n.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        void Walk(string? parentId, string indent)
        {
            var key = parentId ?? string.Empty;
            if (!byParent.TryGetValue(key, out var children))
            {
                return;
            }

            foreach (var node in children.OrderBy(n => n.At))
            {
                var mark = string.Equals(node.Id, tree.TipId, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
                lines.Add($"{indent}{mark} {node.Id}  {node.UserPreview}");
                Walk(node.Id, indent + indentUnit);
            }
        }

        Walk(null, indentUnit);
        return lines;
    }
}
