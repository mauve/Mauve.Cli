using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>Exposes only explicitly allowed values from completed input to a provider.</summary>
public sealed class CompletionContext
{
    private readonly IReadOnlyDictionary<object, object?> _values;
    private readonly ReadOnlyCollection<string> _selectedPath;
    private readonly HashSet<object> _allowedDependencies;

    internal CompletionContext(
        string prefix,
        IReadOnlyList<CommandDefinition> selectedPath,
        IReadOnlyDictionary<object, object?> values,
        IReadOnlyList<CompletionDependency> dependencies)
    {
        Prefix = prefix;
        _selectedPath = Array.AsReadOnly(selectedPath.Select(static command => command.Name).ToArray());
        _values = values;
        Dependencies = dependencies;
        _allowedDependencies = new HashSet<object>(
            dependencies.Select(static dependency => dependency.Option ?? (object)dependency.Positional!),
            ReferenceEqualityComparer.Instance);
    }

    /// <summary>Gets the prefix before the cursor within the active value segment.</summary>
    public string Prefix { get; }

    /// <summary>Gets the canonical selected command path.</summary>
    public IReadOnlyList<string> SelectedPath => _selectedPath;

    /// <summary>Gets the provider's allowed dependency handles.</summary>
    public IReadOnlyList<CompletionDependency> Dependencies { get; }

    /// <summary>Attempts to get an explicitly declared option dependency.</summary>
    public bool TryGetOptionValue<T>(OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return TryGet(option, out value);
    }

    /// <summary>Attempts to get an explicitly declared positional dependency.</summary>
    public bool TryGetPositionalValue<T>(PositionalDefinition positional, out T? value)
    {
        ArgumentNullException.ThrowIfNull(positional);
        return TryGet(positional, out value);
    }

    private bool TryGet<T>(object definition, out T? value)
    {
        if (_allowedDependencies.Contains(definition)
            && _values.TryGetValue(definition, out object? raw)
            && raw is T typed)
        {
            value = typed;
            return true;
        }

        if (_allowedDependencies.Contains(definition)
            && _values.TryGetValue(definition, out raw)
            && raw is null
            && default(T) is null)
        {
            value = default;
            return true;
        }

        value = default;
        return false;
    }
}
