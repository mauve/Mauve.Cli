using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Help;

/// <summary>A sanitized, ordered projection of a command tree.</summary>
public sealed class HelpTreeDocument
{
    private readonly ReadOnlyCollection<HelpTreeItem> _items;

    private HelpTreeDocument(HelpTreeItem[] items)
    {
        _items = Array.AsReadOnly(items);
    }

    /// <summary>Gets the projected items in depth-first order.</summary>
    public IReadOnlyList<HelpTreeItem> Items => _items;

    /// <summary>Projects the visible tree rooted at the selected command.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="command">The selected command, or the root when omitted.</param>
    /// <returns>A sanitized tree projection.</returns>
    public static HelpTreeDocument Create(CommandModel model, CommandDefinition? command = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        CommandDefinition selected = command ?? model.Root;
        if (!IsVisibleMember(model.Root, selected))
        {
            throw new ArgumentException("The command must be a visible member of this model.", nameof(command));
        }

        List<HelpTreeItem> items = [];
        Stack<(CommandDefinition Command, int Depth, bool IsLast)> pending = new();
        if (ReferenceEquals(selected, model.Root))
        {
            pending.Push((selected, 0, true));
        }
        else
        {
            items.Add(CreateItem(model.Root, 0, true));
            items.Add(new HelpTreeItem("... other commands omitted...", string.Empty, 1, false, true));
            pending.Push((selected, 1, true));
        }

        while (pending.Count > 0)
        {
            (CommandDefinition current, int depth, bool isLast) = pending.Pop();
            items.Add(CreateItem(current, depth, isLast));
            bool lastVisible = true;
            for (int index = current.Children.Count - 1; index >= 0; index--)
            {
                CommandDefinition child = current.Children[index];
                if (!child.IsHidden)
                {
                    pending.Push((child, depth + 1, lastVisible));
                    lastVisible = false;
                }
            }
        }

        return new HelpTreeDocument([.. items]);
    }

    private static HelpTreeItem CreateItem(CommandDefinition command, int depth, bool isLast)
        => new(
            HelpTextSanitizer.SingleLine(command.Name),
            HelpTextSanitizer.SingleLine(command.Description ?? string.Empty).Trim(),
            depth,
            isLast,
            false);

    private static bool IsVisibleMember(CommandDefinition root, CommandDefinition selected)
    {
        Stack<CommandDefinition> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            CommandDefinition current = pending.Pop();
            if (current.IsHidden)
            {
                continue;
            }

            if (ReferenceEquals(current, selected))
            {
                return true;
            }

            foreach (CommandDefinition child in current.Children)
            {
                pending.Push(child);
            }
        }

        return false;
    }
}

/// <summary>A sanitized command or omission marker in a help tree.</summary>
public sealed class HelpTreeItem
{
    internal HelpTreeItem(string name, string description, int depth, bool isLast, bool isOmission)
    {
        Name = name;
        Description = description;
        Depth = depth;
        IsLast = isLast;
        IsOmission = isOmission;
    }

    /// <summary>Gets the command name or omission marker.</summary>
    public string Name { get; }

    /// <summary>Gets the single-line sanitized short description.</summary>
    public string Description { get; }

    /// <summary>Gets the depth in the projected tree, with the root at zero.</summary>
    public int Depth { get; }

    /// <summary>Gets whether this is the last child of its projected parent.</summary>
    public bool IsLast { get; }

    /// <summary>Gets whether this item represents omitted commands.</summary>
    public bool IsOmission { get; }
}
