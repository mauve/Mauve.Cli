using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Mauve.Cli.Help;

/// <summary>Renders sanitized help documents as deterministic plain text.</summary>
public static class HelpTextRenderer
{
    /// <summary>Renders a document at the requested width, defaulting to 80 columns.</summary>
    /// <param name="document">The sanitized help document.</param>
    /// <param name="width">The maximum preferred line width.</param>
    /// <returns>Plain text using LF line endings.</returns>
    public static string Render(HelpDocument document, int width = 80)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateWidth(width);

        StringBuilder output = new();
        AppendWrappedLine(output, "Usage: ", document.Usage, width);
        AppendDescription(output, document.Description, width);
        AppendDescription(output, document.LongDescription, width);

        if (document.Children.Count > 0)
        {
            List<HelpDisplayItem> children = [.. document.Children.Select(static child =>
                new HelpDisplayItem(
                    child.Aliases.Count == 0
                        ? child.Name
                        : string.Concat(child.Name, ", ", string.Join(", ", child.Aliases)),
                    child.Description))];
            AppendSection(output, "Commands", string.Empty, children, width);
        }

        if (document.Positionals.Count > 0)
        {
            List<HelpDisplayItem> positionals = [.. document.Positionals.Select(static positional =>
                new HelpDisplayItem(
                    positional.Syntax,
                    AppendMetadata(positional.Description, positional.IsRequired, positional.DefaultDisplay)))];
            AppendSection(output, "Arguments", string.Empty, positionals, width);
        }

        if (document.Directives.Count > 0)
        {
            List<HelpDisplayItem> directives = [.. document.Directives.Select(static directive =>
                new HelpDisplayItem(directive.Syntax, directive.Description))];
            AppendSection(output, "Directives", string.Empty, directives, width);
        }

        foreach (HelpOptionGroup group in document.OptionGroups)
        {
            List<HelpDisplayItem> options = [.. group.Options.Select(static option =>
                new HelpDisplayItem(
                    option.Syntax,
                    AppendMetadata(option.Description, option.IsRequired, option.DefaultDisplay)))];
            string groupDescription = group.Description;
            if (group.Options.Any(static option => option.RequiresEqualsForOptionalValue))
            {
                groupDescription = groupDescription.Length == 0
                    ? "Optional values must use '='."
                    : string.Concat(groupDescription, "\nOptional values must use '='.");
            }

            AppendSection(output, group.Title, groupDescription, options, width);
        }

        return output.ToString();
    }

    internal static void ValidateWidth(int width)
    {
        if (width < 20)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Help width must be at least 20 columns.");
        }
    }

    private static string AppendMetadata(string description, bool isRequired, string? defaultDisplay)
    {
        List<string> metadata = [];
        if (isRequired)
        {
            metadata.Add("required");
        }

        if (defaultDisplay is not null)
        {
            metadata.Add(string.Concat("default: ", defaultDisplay));
        }

        if (metadata.Count == 0)
        {
            return description;
        }

        string suffix = string.Concat(" (", string.Join("; ", metadata), ")");
        return description.Length == 0 ? suffix.TrimStart() : string.Concat(description, suffix);
    }

    private static void AppendDescription(StringBuilder output, string description, int width)
    {
        if (description.Length == 0)
        {
            return;
        }

        foreach (string line in Wrap(description, width))
        {
            AppendLine(output, line);
        }
    }

    private static void AppendSection(
        StringBuilder output,
        string title,
        string description,
        IReadOnlyList<HelpDisplayItem> items,
        int width)
    {
        if (items.Count == 0)
        {
            return;
        }

        foreach (string line in Wrap(string.Concat(title, ":"), width))
        {
            AppendLine(output, line);
        }
        if (description.Length > 0)
        {
            foreach (string line in Wrap(description, Math.Max(1, width - 2)))
            {
                AppendLine(output, string.Concat("  ", line));
            }
        }

        int labelWidth = items.Max(static item => item.Label.Length);
        bool alignDescriptions = labelWidth + 6 < width;
        int descriptionColumn = alignDescriptions ? labelWidth + 4 : 4;
        int descriptionWidth = Math.Max(1, width - descriptionColumn);

        foreach (HelpDisplayItem item in items)
        {
            string prefix;
            string descriptionPrefix;
            if (alignDescriptions)
            {
                prefix = string.Concat("  ", item.Label.PadRight(labelWidth), "  ");
                descriptionPrefix = new string(' ', descriptionColumn);
            }
            else
            {
                prefix = string.Concat("  ", item.Label);
                descriptionPrefix = "    ";
            }

            if (item.Description.Length == 0)
            {
                if (alignDescriptions)
                {
                    AppendLine(output, prefix);
                }
                else
                {
                    foreach (string labelLine in Wrap(item.Label, Math.Max(1, width - 2)))
                    {
                        AppendLine(output, string.Concat("  ", labelLine));
                    }
                }

                continue;
            }

            List<string> wrapped = Wrap(item.Description, descriptionWidth);
            if (alignDescriptions)
            {
                AppendLine(output, string.Concat(prefix, wrapped[0]));
            }
            else
            {
                foreach (string labelLine in Wrap(item.Label, Math.Max(1, width - 2)))
                {
                    AppendLine(output, string.Concat("  ", labelLine));
                }

                AppendLine(output, string.Concat(descriptionPrefix, wrapped[0]));
            }

            for (int index = 1; index < wrapped.Count; index++)
            {
                AppendLine(output, string.Concat(descriptionPrefix, wrapped[index]));
            }
        }
    }

    private static List<string> Wrap(string text, int width)
    {
        List<string> lines = [];
        foreach (string paragraph in text.Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            StringBuilder line = new();
            foreach (string word in words)
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    lines.Add(line.ToString());
                    _ = line.Clear();
                }
                else if (line.Length > 0)
                {
                    _ = line.Append(' ');
                }

                int wordOffset = 0;
                while (wordOffset < word.Length)
                {
                    int count = Math.Min(width - line.Length, word.Length - wordOffset);
                    _ = line.Append(word, wordOffset, count);
                    wordOffset += count;
                    if (line.Length == width)
                    {
                        lines.Add(line.ToString());
                        _ = line.Clear();
                    }
                }
            }

            if (line.Length > 0)
            {
                lines.Add(line.ToString());
            }
        }

        return lines;
    }

    private static void AppendWrappedLine(StringBuilder output, string prefix, string text, int width)
    {
        List<string> lines = Wrap(text, Math.Max(1, width - prefix.Length));
        AppendLine(output, string.Concat(prefix, lines[0]));
        string continuation = new(' ', prefix.Length);
        for (int index = 1; index < lines.Count; index++)
        {
            AppendLine(output, string.Concat(continuation, lines[index]));
        }
    }

    private static void AppendLine(StringBuilder output, string line) => _ = output.Append(line).Append('\n');

    private sealed record HelpDisplayItem(string Label, string Description);
}
