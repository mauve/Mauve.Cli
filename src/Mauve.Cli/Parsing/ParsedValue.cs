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
        int[] tokenIndices,
        ValueCoordinate[]? valueCoordinates = null)
    {
        Owner = owner;
        Option = option;
        Positional = positional;
        Value = value;
        IsAvailable = isAvailable;
        Presence = presence;
        Origin = origin;
        _tokenIndices = Array.AsReadOnly(tokenIndices);
        ValueCoordinates = valueCoordinates ?? [];
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

    /// <summary>Attempts to get this entry's available value as the requested type.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="value">The value when available, which may be null.</param>
    /// <returns>True when the value is available, including an available null.</returns>
    /// <exception cref="InvalidOperationException">The available value is incompatible with the requested type.</exception>
    public bool TryGetValue<T>(out T? value)
    {
        if (!IsAvailable)
        {
            value = default;
            return false;
        }

        if (Value is T typedValue)
        {
            value = typedValue;
            return true;
        }

        if (Value is null && default(T) is null)
        {
            value = default;
            return true;
        }

        throw new InvalidOperationException("The requested value type does not match the definition.");
    }

    internal IReadOnlyList<ValueCoordinate> ValueCoordinates { get; }
}

internal readonly record struct ValueCoordinate(int? OptionIndex, int? ValueIndex, int? ValueOffset, int ValueLength);
