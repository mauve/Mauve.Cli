using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Parsing;

/// <summary>An immutable value entry bound to a definition and owner.</summary>
public sealed class ParsedValue
{
    private readonly ReadOnlyCollection<int> _tokenIndices;

    internal ParsedValue(
        CommandDefinition owner,
        OptionDefinition? option,
        PositionalDefinition? positional,
        object? value,
        bool isAvailable,
        ValuePresence presence,
        ValueOrigin origin,
        int[] tokenIndices)
    {
        Owner = owner;
        Option = option;
        Positional = positional;
        Value = value;
        IsAvailable = isAvailable;
        Presence = presence;
        Origin = origin;
        _tokenIndices = Array.AsReadOnly(tokenIndices);
    }

    /// <summary>Gets the command that owns the definition.</summary>
    public CommandDefinition Owner { get; }

    /// <summary>Gets the option definition, when this entry is an option.</summary>
    public OptionDefinition? Option { get; }

    /// <summary>Gets the positional definition, when this entry is positional.</summary>
    public PositionalDefinition? Positional { get; }

    /// <summary>Gets the typed value when available.</summary>
    public object? Value { get; }

    /// <summary>Gets whether the value is available.</summary>
    public bool IsAvailable { get; }

    /// <summary>Gets how the value was supplied.</summary>
    public ValuePresence Presence { get; }

    /// <summary>Gets where the value came from.</summary>
    public ValueOrigin Origin { get; }

    /// <summary>Gets the original argv indices that supplied the value.</summary>
    public IReadOnlyList<int> TokenIndices => _tokenIndices;
}
