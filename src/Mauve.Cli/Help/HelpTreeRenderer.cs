using System;
using System.Collections.Generic;
using System.Text;

namespace Mauve.Cli.Help;

/// <summary>Renders a sanitized command tree with Unicode relationship connectors.</summary>
public static class HelpTreeRenderer
{
    /// <summary>Renders a tree using LF line endings.</summary>
    /// <param name="document">The sanitized tree projection.</param>
    /// <returns>Plain text without a trailing line ending.</returns>
    public static string Render(HelpTreeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StringBuilder output = new();
        List<bool> lastAtDepth = [];
        foreach (HelpTreeItem item in document.Items)
        {
            if (output.Length > 0)
            {
                _ = output.Append('\n');
            }

            for (int depth = 1; depth < item.Depth; depth++)
            {
                _ = output.Append(lastAtDepth[depth] ? "    " : "│   ");
            }

            if (item.Depth > 0)
            {
                _ = output.Append(item.IsLast ? "└── " : "├── ");
            }

            _ = output.Append(item.Name);
            if (item.Description.Length > 0)
            {
                _ = output.Append(" (").Append(item.Description).Append(')');
            }

            while (lastAtDepth.Count <= item.Depth)
            {
                lastAtDepth.Add(false);
            }

            lastAtDepth[item.Depth] = item.IsLast;
        }

        return output.ToString();
    }
}
