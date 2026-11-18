using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>Builds an immutable registry of explicitly scoped completion providers.</summary>
public sealed class CompletionProviderRegistryBuilder
{
    private readonly Dictionary<object, ProviderRegistration> _providers =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Registers a provider for an option definition.</summary>
    public CompletionProviderRegistryBuilder Register(
        OptionDefinition option,
        CompletionProvider provider,
        params CompletionDependency[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(provider);
        ValidateTarget(option.IsHidden, option.IsSensitive, nameof(option));
        return Add(option, provider, dependencies);
    }

    /// <summary>Registers a provider for a positional definition.</summary>
    public CompletionProviderRegistryBuilder Register(
        PositionalDefinition positional,
        CompletionProvider provider,
        params CompletionDependency[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(positional);
        ArgumentNullException.ThrowIfNull(provider);
        ValidateTarget(positional.IsHidden, positional.IsSensitive, nameof(positional));
        return Add(positional, provider, dependencies);
    }

    /// <summary>Creates an immutable provider registry.</summary>
    public CompletionProviderRegistry Build()
    {
        return new CompletionProviderRegistry(new Dictionary<object, ProviderRegistration>(
            _providers,
            ReferenceEqualityComparer.Instance));
    }

    private CompletionProviderRegistryBuilder Add(
        object target,
        CompletionProvider provider,
        CompletionDependency[] dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
        List<CompletionDependency> copied = [];
        foreach (CompletionDependency dependency in dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependency);
            object handle = dependency.Option ?? (object?)dependency.Positional
                ?? throw new ArgumentException("A completion dependency has no definition handle.", nameof(dependencies));
            bool hidden = dependency.Option?.IsHidden ?? dependency.Positional!.IsHidden;
            bool sensitive = dependency.Option?.IsSensitive ?? dependency.Positional!.IsSensitive;
            ValidateTarget(hidden, sensitive, nameof(dependencies));
            if (!seen.Add(handle))
            {
                throw new ArgumentException("Completion dependencies must be unique.", nameof(dependencies));
            }

            copied.Add(dependency);
        }

        if (!_providers.TryAdd(target, new ProviderRegistration(provider, Array.AsReadOnly(copied.ToArray()))))
        {
            throw new InvalidOperationException("A completion provider is already registered for this definition.");
        }

        return this;
    }

    private static void ValidateTarget(bool isHidden, bool isSensitive, string parameterName)
    {
        if (isHidden || isSensitive)
        {
            throw new ArgumentException("Hidden or sensitive definitions cannot use completion providers.", parameterName);
        }
    }
}

/// <summary>An immutable registry of explicitly scoped completion providers.</summary>
public sealed class CompletionProviderRegistry
{
    private readonly ReadOnlyDictionary<object, ProviderRegistration> _providers;

    internal CompletionProviderRegistry(Dictionary<object, ProviderRegistration> providers)
    {
        _providers = new ReadOnlyDictionary<object, ProviderRegistration>(providers);
    }

    /// <summary>Creates an empty provider registry.</summary>
    public static CompletionProviderRegistry Empty()
    {
        return new CompletionProviderRegistry(new Dictionary<object, ProviderRegistration>(
            ReferenceEqualityComparer.Instance));
    }

    internal bool TryGet(object definition, out ProviderRegistration registration)
    {
        return _providers.TryGetValue(definition, out registration!);
    }
}

internal sealed record ProviderRegistration(
    CompletionProvider Provider,
    IReadOnlyList<CompletionDependency> Dependencies);
