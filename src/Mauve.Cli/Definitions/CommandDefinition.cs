using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>An immutable command in a validated model snapshot.</summary>
public sealed class CommandDefinition
{
    private readonly ReadOnlyCollection<string> _aliases;
    private readonly ReadOnlyCollection<CommandDefinition> _children;
    private readonly ReadOnlyCollection<OptionDefinition> _options;
    private readonly ReadOnlyCollection<PositionalDefinition> _positionals;
    private readonly ReadOnlyCollection<OptionPackSnapshot> _optionPacks;
    private readonly ReadOnlyCollection<CrossValueValidator> _crossValidators;
    private readonly ReadOnlyCollection<string> _path;
    private readonly Dictionary<string, OptionDefinition> _optionsByName;
    private readonly Dictionary<OptionDefinition, OptionPackGroupMetadata> _packGroups;

    internal CommandDefinition(
        CommandBuilder builder,
        string[] aliases,
        CommandDefinition[] children,
        OptionDefinition[] options,
        PositionalDefinition[] positionals,
        CrossValueValidator[] crossValidators,
        CommandHandler? handler,
        string[] path,
        OptionPackSnapshot[] optionPacks,
        Dictionary<OptionDefinition, OptionPackGroupMetadata> packGroups)
    {
        Name = builder.Name;
        Description = builder.Description;
        LongDescription = builder.LongDescription;
        IsHidden = builder.IsHidden;
        _aliases = Array.AsReadOnly(aliases);
        _children = Array.AsReadOnly(children);
        _options = Array.AsReadOnly(options);
        _positionals = Array.AsReadOnly(positionals);
        _optionPacks = Array.AsReadOnly(optionPacks);
        _crossValidators = Array.AsReadOnly(crossValidators);
        _packGroups = packGroups;
        Handler = handler;
        _path = Array.AsReadOnly(path);
        _optionsByName = [with(StringComparer.Ordinal)];
        foreach (OptionDefinition option in options)
        {
            _optionsByName.Add(option.Name, option);
            foreach (OptionAlias alias in option.Aliases)
            {
                _optionsByName.Add(alias.Name, option);
            }
        }
    }

    /// <summary>Gets the canonical name or root display label.</summary>
    public string Name { get; }

    /// <summary>Gets the ordered aliases.</summary>
    public IReadOnlyList<string> Aliases => _aliases;

    /// <summary>Gets the short description.</summary>
    public string? Description { get; }

    /// <summary>Gets the long description.</summary>
    public string? LongDescription { get; }

    /// <summary>Gets whether ordinary help and completion hide this command.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets the ordered children.</summary>
    public IReadOnlyList<CommandDefinition> Children => _children;

    /// <summary>Gets the ordered local options.</summary>
    public IReadOnlyList<OptionDefinition> Options => _options;

    /// <summary>Gets the ordered positional arguments.</summary>
    public IReadOnlyList<PositionalDefinition> Positionals => _positionals;

    /// <summary>Gets the ordered option-pack inclusions owned by this command.</summary>
    internal IReadOnlyList<OptionPackSnapshot> OptionPacks => _optionPacks;

    /// <summary>Gets the ordered cross-value validators for this command.</summary>
    public IReadOnlyList<CrossValueValidator> CrossValidators => _crossValidators;

    internal CommandHandler? Handler { get; }

    internal bool TryGetPackGroup(OptionDefinition option, out OptionPackGroupMetadata group)
    {
        return _packGroups.TryGetValue(option, out group);
    }

    /// <summary>Gets the canonical path components from the root to this command.</summary>
    public IReadOnlyList<string> Path => _path;

    /// <summary>Finds a local option by its canonical name or alias.</summary>
    /// <param name="name">The exact option spelling.</param>
    /// <param name="option">The matching option, if found.</param>
    /// <returns><see langword="true"/> when a matching option exists.</returns>
    public bool TryGetOption(string name, [NotNullWhen(true)] out OptionDefinition? option)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _optionsByName.TryGetValue(name, out option);
    }
}
