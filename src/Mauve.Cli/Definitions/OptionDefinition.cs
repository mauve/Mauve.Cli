using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Conversion;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>Describes one option independently of any parse invocation.</summary>
public sealed class OptionDefinition
{
    private readonly ReadOnlyCollection<OptionAlias> _aliases;
    private readonly ReadOnlyCollection<char> _shortAliases;
    private readonly ReadOnlyCollection<IValueValidator> _validators;

    /// <summary>Creates an immutable option definition.</summary>
    /// <param name="name">The prefix-free canonical long option name, or null for a short-only option.</param>
    /// <param name="valueType">The type of the option value.</param>
    /// <param name="arity">The option's token and repetition mode.</param>
    /// <param name="aliases">Optional ordered aliases.</param>
    /// <param name="elementType">The element type for collection options.</param>
    /// <param name="allowsNull">Whether this definition permits a null value.</param>
    /// <param name="isRequired">Whether an occurrence is required.</param>
    /// <param name="description">Optional display description.</param>
    /// <param name="valueLabel">Optional display label for a value.</param>
    /// <param name="isHidden">Whether ordinary help and completion hide this option.</param>
    /// <param name="isSensitive">Whether values must be treated as sensitive.</param>
    /// <param name="bareValuePolicy">The behavior for an optional option used bare.</param>
    /// <param name="bareValue">The constant used by <see cref="BareValuePolicy.Constant"/>.</param>
    /// <param name="bareValueFactory">The factory used by <see cref="BareValuePolicy.Factory"/>.</param>
    /// <param name="defaultValuePolicy">The behavior when this option is absent.</param>
    /// <param name="defaultValue">The constant used by <see cref="DefaultValuePolicy.Constant"/>.</param>
    /// <param name="defaultValueFactory">The factory used by <see cref="DefaultValuePolicy.Factory"/>.</param>
    /// <param name="validators">Ordered validators for available values.</param>
    /// <param name="defaultDisplay">Explicit non-sensitive text allowed in generated help.</param>
    /// <param name="displayGroup">Optional stable group metadata for generated help.</param>
    /// <param name="scope">Whether descendants can see this option.</param>
    /// <param name="converter">An exact scalar or collection-element conversion override.</param>
    /// <param name="shortName">The primary short option character.</param>
    /// <param name="shortAliases">Additional short option characters.</param>
    /// <param name="environmentVariableBinding">Whether environment binding is enabled, disabled, or inherited from the host.</param>
    /// <param name="environmentVariableName">An optional full environment-variable name override.</param>
    public OptionDefinition(
        string? name,
        Type valueType,
        OptionArity arity,
        IEnumerable<OptionAlias>? aliases = null,
        Type? elementType = null,
        bool allowsNull = false,
        bool isRequired = false,
        string? description = null,
        string? valueLabel = null,
        bool isHidden = false,
        bool isSensitive = false,
        BareValuePolicy bareValuePolicy = BareValuePolicy.Unspecified,
        object? bareValue = null,
        Func<object?>? bareValueFactory = null,
        DefaultValuePolicy defaultValuePolicy = DefaultValuePolicy.None,
        object? defaultValue = null,
        Func<object?>? defaultValueFactory = null,
        IEnumerable<IValueValidator>? validators = null,
        string? defaultDisplay = null,
        OptionDisplayGroup? displayGroup = null,
        OptionScope scope = OptionScope.Local,
        ValueConverterRegistration? converter = null,
        char? shortName = null,
        IEnumerable<char>? shortAliases = null,
        EnvironmentVariableBinding environmentVariableBinding = EnvironmentVariableBinding.Inherit,
        string? environmentVariableName = null)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        string? configuredName = name;
        if (name?.StartsWith("--", StringComparison.Ordinal) == true)
        {
            name = name[2..];
        }
        else if (name is { Length: 2 } && name[0] == '-')
        {
            shortName ??= name[1];
            name = null;
        }

        LongName = name;
        Name = name is not null
            ? string.Concat("--", name)
            : shortName is char primaryShort ? string.Concat("-", primaryShort) : configuredName ?? string.Empty;
        ShortName = shortName;
        ValueType = valueType;
        Arity = arity;
        ElementType = elementType;
        AllowsNull = allowsNull;
        IsRequired = isRequired;
        Description = description;
        ValueLabel = valueLabel;
        IsHidden = isHidden;
        IsSensitive = isSensitive;
        BareValuePolicy = bareValuePolicy;
        BareValue = bareValue;
        BareValueFactory = bareValueFactory;
        DefaultValuePolicy = defaultValuePolicy;
        DefaultValue = defaultValue;
        DefaultValueFactory = defaultValueFactory;
        DefaultDisplay = defaultDisplay;
        DisplayGroup = displayGroup;
        Scope = scope;
        Converter = converter;
        if (!Enum.IsDefined(environmentVariableBinding))
        {
            throw new ArgumentOutOfRangeException(nameof(environmentVariableBinding));
        }

        if (environmentVariableName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
            if (environmentVariableName.Contains('=', StringComparison.Ordinal)
                || environmentVariableName.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("Environment-variable names cannot contain '=' or null characters.", nameof(environmentVariableName));
            }
        }

        EnvironmentVariableBinding = environmentVariableBinding;
        EnvironmentVariableName = environmentVariableName;

        List<OptionAlias> aliasCopy = [];
        List<char> shortAliasCopy = shortAliases is null ? [] : [with(shortAliases)];
        if (aliases is not null)
        {
            foreach (OptionAlias alias in aliases)
            {
                if (alias is null)
                {
                    throw new ArgumentException("Option aliases cannot contain null entries.", nameof(aliases));
                }

                if (alias.Name.StartsWith("--", StringComparison.Ordinal))
                {
                    aliasCopy.Add(new OptionAlias(alias.Name[2..], alias.Kind));
                }
                else if (alias.Name.Length == 2 && alias.Name[0] == '-')
                {
                    if (alias.Kind != OptionAliasKind.Normal)
                    {
                        throw new ArgumentException("Short aliases cannot have negating meaning.", nameof(aliases));
                    }

                    shortAliasCopy.Add(alias.Name[1]);
                }
                else
                {
                    aliasCopy.Add(alias);
                }
            }
        }

        if (aliasCopy.Exists(static alias => alias is null))
        {
            throw new ArgumentException("Option aliases cannot contain null entries.", nameof(aliases));
        }

        _aliases = aliasCopy.AsReadOnly();
        _shortAliases = shortAliasCopy.AsReadOnly();
        List<IValueValidator> validatorCopy = validators is null ? [] : [with(validators)];
        if (validatorCopy.Exists(static validator => validator is null))
        {
            throw new ArgumentException("Option validators cannot contain null entries.", nameof(validators));
        }

        _validators = validatorCopy.AsReadOnly();
    }

    /// <summary>Gets the canonical long option name.</summary>
    /// <summary>Gets the canonical long name, or the primary short character when short-only.</summary>
    public string Name { get; }

    /// <summary>Gets the prefix-free canonical long name, when one is declared.</summary>
    public string? LongName { get; }

    /// <summary>Gets the primary short option character.</summary>
    public char? ShortName { get; }

    /// <summary>Gets the type of the option value.</summary>
    public Type ValueType { get; }

    /// <summary>Gets the option's token and repetition mode.</summary>
    public OptionArity Arity { get; }

    /// <summary>Gets the ordered aliases.</summary>
    public IReadOnlyList<OptionAlias> Aliases => _aliases;

    /// <summary>Gets the ordered short option aliases.</summary>
    public IReadOnlyList<char> ShortAliases => _shortAliases;

    internal string DisplayName => Name;

    /// <summary>Gets the collection element type, when applicable.</summary>
    public Type? ElementType { get; }

    /// <summary>Gets whether this definition permits a null value.</summary>
    public bool AllowsNull { get; }

    /// <summary>Gets whether an occurrence is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the display description.</summary>
    public string? Description { get; }

    /// <summary>Gets the display label for a value.</summary>
    public string? ValueLabel { get; }

    /// <summary>Gets whether ordinary help and completion hide this option.</summary>
    public bool IsHidden { get; }

    /// <summary>Gets whether values must be treated as sensitive.</summary>
    public bool IsSensitive { get; }

    /// <summary>Gets the behavior for an optional option used bare.</summary>
    public BareValuePolicy BareValuePolicy { get; }

    /// <summary>Gets the constant used by <see cref="BareValuePolicy.Constant"/>.</summary>
    public object? BareValue { get; }

    /// <summary>Gets the factory used by <see cref="BareValuePolicy.Factory"/>.</summary>
    public Func<object?>? BareValueFactory { get; }

    /// <summary>Gets the behavior when this option is absent.</summary>
    public DefaultValuePolicy DefaultValuePolicy { get; }

    /// <summary>Gets the constant used by <see cref="DefaultValuePolicy.Constant"/>.</summary>
    public object? DefaultValue { get; }

    /// <summary>Gets the factory used by <see cref="DefaultValuePolicy.Factory"/>.</summary>
    public Func<object?>? DefaultValueFactory { get; }

    /// <summary>Gets the ordered validators for available values.</summary>
    public IReadOnlyList<IValueValidator> Validators => _validators;

    /// <summary>Gets explicit display text for a declared default, when allowed.</summary>
    public string? DefaultDisplay { get; }

    /// <summary>Gets optional stable help-group metadata.</summary>
    public OptionDisplayGroup? DisplayGroup { get; }

    /// <summary>Gets whether the option is local to its owner or inherited by descendants.</summary>
    public OptionScope Scope { get; }

    /// <summary>Gets the definition-local scalar or element converter.</summary>
    public ValueConverterRegistration? Converter { get; }

    /// <summary>Gets whether environment-variable binding is enabled, disabled, or inherited from the host.</summary>
    public EnvironmentVariableBinding EnvironmentVariableBinding { get; }

    /// <summary>Gets the optional full environment-variable name override.</summary>
    public string? EnvironmentVariableName { get; }

    internal bool IsHelpOption { get; private init; }

    internal bool IsHelpTreeOption { get; private init; }

    internal static OptionDefinition CreateStandardHelp()
    {
        return new OptionDefinition(
            "help",
            typeof(bool),
            OptionArity.BooleanFlag,
            aliases: [],
            shortName: 'h',
            environmentVariableBinding: EnvironmentVariableBinding.Disabled)
        {
            IsHelpOption = true
        };
    }

    internal static OptionDefinition CreateStandardHelpTree()
    {
        return new OptionDefinition(
            "help-tree",
            typeof(bool),
            OptionArity.BooleanFlag,
            environmentVariableBinding: EnvironmentVariableBinding.Disabled)
        {
            IsHelpTreeOption = true
        };
    }
}
