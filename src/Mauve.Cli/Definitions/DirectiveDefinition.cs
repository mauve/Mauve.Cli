using System;
using System.Collections.ObjectModel;

namespace Mauve.Cli.Definitions;

/// <summary>An immutable directive registration on a command model root.</summary>
public sealed class DirectiveDefinition
{
    internal DirectiveDefinition(
        string name,
        string? description,
        bool isHidden,
        DirectiveValueDefinition[] values,
        DirectiveHandler? handler,
        bool isCompletion)
    {
        Name = name;
        Description = description;
        IsHidden = isHidden;
        Values = Array.AsReadOnly(values);
        Handler = handler;
        IsCompletion = isCompletion;
    }

    /// <summary>Gets the canonical directive name.</summary>
    public string Name { get; }

    /// <summary>Gets the display description.</summary>
    public string? Description { get; }

    /// <summary>Gets whether help and completion hide this directive.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets ordered bare value definitions.</summary>
    public ReadOnlyCollection<DirectiveValueDefinition> Values { get; }

    internal DirectiveHandler? Handler { get; }

    internal bool IsCompletion { get; }
}
