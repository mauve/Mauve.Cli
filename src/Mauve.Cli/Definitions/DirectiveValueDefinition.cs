using System;

namespace Mauve.Cli.Definitions;

/// <summary>Describes one ordered bare value accepted by a directive.</summary>
public sealed class DirectiveValueDefinition
{
    /// <summary>Creates a directive value definition.</summary>
    /// <param name="name">The value's inferred or explicit name.</param>
    /// <param name="valueType">The scalar type or IReadOnlyList&lt;T&gt; type.</param>
    /// <param name="isRequired">Whether a scalar value is required.</param>
    /// <param name="description">Optional display description.</param>
    /// <param name="isHidden">Whether help and completion hide this value.</param>
    /// <param name="isSensitive">Whether the value is sensitive.</param>
    public DirectiveValueDefinition(
        string name,
        Type valueType,
        bool isRequired = true,
        string? description = null,
        bool isHidden = false,
        bool isSensitive = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueType);
        Name = name;
        ValueType = valueType;
        ElementType = valueType.IsGenericType
            && valueType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IReadOnlyList<>)
                ? valueType.GetGenericArguments()[0]
                : null;
        IsRequired = isRequired;
        Description = description;
        IsHidden = isHidden;
        IsSensitive = isSensitive;
    }

    /// <summary>Gets the canonical value name.</summary>
    public string Name { get; }

    /// <summary>Gets the scalar value type or collection type.</summary>
    public Type ValueType { get; }

    /// <summary>Gets the element type for a collection value.</summary>
    public Type? ElementType { get; }

    /// <summary>Gets whether a scalar value is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the display description.</summary>
    public string? Description { get; }

    /// <summary>Gets whether help and completion hide this value.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets whether supplied values are sensitive.</summary>
    public bool IsSensitive { get; }
}
