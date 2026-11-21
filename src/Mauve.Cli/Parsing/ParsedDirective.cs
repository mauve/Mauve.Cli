using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Parsing;

/// <summary>A validated directive occurrence and its typed values.</summary>
public sealed class ParsedDirective
{
    private readonly ReadOnlyCollection<object?> _values;
    private readonly ReadOnlyCollection<int> _tokenIndices;

    internal ParsedDirective(DirectiveDefinition definition, object?[] values, int[] tokenIndices)
    {
        Definition = definition;
        _values = Array.AsReadOnly(values);
        _tokenIndices = Array.AsReadOnly(tokenIndices);
    }

    /// <summary>Gets the registered directive definition.</summary>
    public DirectiveDefinition Definition { get; }

    /// <summary>Gets typed values in declaration order.</summary>
    public IReadOnlyList<object?> Values => _values;

    /// <summary>Gets the original argv token indices occupied by this directive.</summary>
    public IReadOnlyList<int> TokenIndices => _tokenIndices;

    /// <summary>Gets a typed value by its definition handle.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="definition">A value definition belonging to this directive.</param>
    /// <param name="value">The value, when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetValue<T>(DirectiveValueDefinition definition, out T? value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        int index = -1;
        for (int candidate = 0; candidate < Definition.Values.Count; candidate++)
        {
            if (ReferenceEquals(Definition.Values[candidate], definition))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            throw new ArgumentException("The value definition does not belong to this directive.", nameof(definition));
        }

        object? item = _values[index];
        if (item is T typed)
        {
            value = typed;
            return true;
        }

        if (item is null && default(T) is null)
        {
            value = default;
            return true;
        }

        throw new InvalidOperationException("The requested value type does not match the definition.");
    }
}
