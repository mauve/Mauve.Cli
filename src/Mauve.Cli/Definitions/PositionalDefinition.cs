using System;

namespace Mauve.Cli.Definitions;

/// <summary>Describes one ordered positional argument.</summary>
public sealed class PositionalDefinition
{
    /// <summary>Creates an immutable positional argument definition.</summary>
    /// <param name="name">The diagnostic name of the positional argument.</param>
    /// <param name="valueType">The bound value type.</param>
    /// <param name="minimumValues">The minimum number of values accepted.</param>
    /// <param name="maximumValues">The maximum number, or <see langword="null"/> for a rest argument.</param>
    /// <param name="elementType">The element type for a rest argument.</param>
    /// <param name="allowsNull">Whether this definition permits a null value.</param>
    /// <param name="description">Optional display description.</param>
    /// <param name="isHidden">Whether ordinary help and completion hide this argument.</param>
    /// <param name="isSensitive">Whether values must be treated as sensitive.</param>
    public PositionalDefinition(
        string name,
        Type valueType,
        int minimumValues = 1,
        int? maximumValues = 1,
        Type? elementType = null,
        bool allowsNull = false,
        string? description = null,
        bool isHidden = false,
        bool isSensitive = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueType);

        Name = name;
        ValueType = valueType;
        MinimumValues = minimumValues;
        MaximumValues = maximumValues;
        ElementType = elementType;
        AllowsNull = allowsNull;
        Description = description ?? string.Empty;
        IsHidden = isHidden;
        IsSensitive = isSensitive;
    }

    /// <summary>Gets the diagnostic name of the positional argument.</summary>
    public string Name { get; }

    /// <summary>Gets the bound value type.</summary>
    public Type ValueType { get; }

    /// <summary>Gets the minimum number of values accepted.</summary>
    public int MinimumValues { get; }

    /// <summary>Gets the maximum number of values, or <see langword="null"/> for a rest argument.</summary>
    public int? MaximumValues { get; }

    /// <summary>Gets the element type for a rest argument.</summary>
    public Type? ElementType { get; }

    /// <summary>Gets whether this definition permits a null value.</summary>
    public bool AllowsNull { get; }

    /// <summary>Gets the display description.</summary>
    public string Description { get; }

    /// <summary>Gets whether ordinary help and completion hide this argument.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets whether values must be treated as sensitive.</summary>
    public bool IsSensitive { get; }
}
