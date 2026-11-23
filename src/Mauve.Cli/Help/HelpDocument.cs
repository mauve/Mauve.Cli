using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Mauve.Cli.Help;

/// <summary>Sanitized, sensitivity-filtered help data projected from a command model.</summary>
public sealed class HelpDocument
{
    private readonly ReadOnlyCollection<HelpCommandItem> _children;
    private readonly ReadOnlyCollection<HelpPositionalItem> _positionals;
    private readonly ReadOnlyCollection<HelpOptionGroup> _optionGroups;
    private readonly ReadOnlyCollection<HelpDirectiveItem> _directives;
    private readonly ReadOnlyCollection<string> _examples;

    internal HelpDocument(
        string usage,
        string description,
        string longDescription,
        HelpCommandItem[] children,
        HelpPositionalItem[] positionals,
        HelpOptionGroup[] optionGroups,
        HelpDirectiveItem[] directives,
        string[] examples)
    {
        Usage = usage;
        Description = description;
        LongDescription = longDescription;
        _children = Array.AsReadOnly(children);
        _positionals = Array.AsReadOnly(positionals);
        _optionGroups = Array.AsReadOnly(optionGroups);
        _directives = Array.AsReadOnly(directives);
        _examples = Array.AsReadOnly(examples);
    }

    /// <summary>Gets the deterministic usage line.</summary>
    public string Usage { get; }

    /// <summary>Gets the sanitized short description.</summary>
    public string Description { get; }

    /// <summary>Gets the sanitized long description.</summary>
    public string LongDescription { get; }

    /// <summary>Gets visible child command summaries in declaration order.</summary>
    public IReadOnlyList<HelpCommandItem> Children => _children;

    /// <summary>Gets visible positional summaries in declaration order.</summary>
    public IReadOnlyList<HelpPositionalItem> Positionals => _positionals;

    /// <summary>Gets visible option groups in first-member order.</summary>
    public IReadOnlyList<HelpOptionGroup> OptionGroups => _optionGroups;

    /// <summary>Gets visible directive syntax in root registration order.</summary>
    public IReadOnlyList<HelpDirectiveItem> Directives => _directives;

    /// <summary>Gets ordered, sanitized example invocations with canonical ancestors and executable substitutions.</summary>
    public IReadOnlyList<string> Examples => _examples;

    /// <summary>Creates a sanitized help projection for the root or a command in the model.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="command">The command to describe, or the root when omitted.</param>
    /// <param name="executableName">The executable display name for usage and <c>$0</c>, or the root display label when omitted.</param>
    /// <param name="syntax">The option prefixes used to display option spellings.</param>
    /// <param name="hostOptions">The host settings used to determine environment-variable names and enablement.</param>
    /// <returns>A read-only help document.</returns>
    public static HelpDocument Create(
        CommandModel model,
        CommandDefinition? command = null,
        string? executableName = null,
        CliSyntaxOptions? syntax = null,
        CliHostOptions? hostOptions = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (executableName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        }
        CommandDefinition selected = command ?? model.Root;
        syntax ??= CliSyntaxOptions.Default;
        hostOptions ??= new CliHostOptions();
        if (!Contains(model.Root, selected))
        {
            throw new ArgumentException("The command does not belong to this model.", nameof(command));
        }

        if (selected.IsHidden)
        {
            throw new ArgumentException("Help cannot be created for a hidden command.", nameof(command));
        }

        HelpCommandItem[] children = [.. selected.Children
            .Where(static child => !child.IsHidden)
            .Select(static child => new HelpCommandItem(
                HelpTextSanitizer.SingleLine(child.Name),
                [.. child.Aliases.Select(HelpTextSanitizer.SingleLine)],
                HelpTextSanitizer.Description(child.Description)))];

        HelpPositionalItem[] positionals = [.. selected.Positionals
            .Where(static positional => !positional.IsHidden)
            .Select(static positional => CreatePositionalItem(positional))];
        HelpDirectiveItem[] directives = [.. model.Directives
            .Where(static directive => !directive.IsHidden)
            .Select(CreateDirectiveItem)];

        List<HelpOptionGroupBuilder> groups = [];
        Dictionary<(CommandDefinition Owner, string Key), HelpOptionGroupBuilder> groupsByKey = [];
        IReadOnlyList<CommandOption> effectiveOptions = CommandModel.GetEffectiveOptions(model.GetPath(selected));
        string prefix = NormalizeEnvironmentName(hostOptions.EnvironmentVariablePrefix ?? model.Root.Name);
        foreach (CommandOption ownedOption in effectiveOptions)
        {
            OptionDefinition option = ownedOption.Definition;
            if (option.IsHidden)
            {
                continue;
            }

            bool environmentBindingEnabled = hostOptions.EnableEnvironmentVariables
                && option.EnvironmentVariableBinding switch
                {
                    EnvironmentVariableBinding.Inherit => hostOptions.EnvironmentVariablesEnabledByDefault,
                    EnvironmentVariableBinding.Enabled => true,
                    EnvironmentVariableBinding.Disabled => false,
                    _ => throw new InvalidOperationException("The option has an unsupported environment-variable binding policy.")
                };
            string? environmentVariableName = null;
            if (environmentBindingEnabled)
            {
                environmentVariableName = option.EnvironmentVariableName;
                if (environmentVariableName is null && option.LongName is string longName)
                {
                    environmentVariableName = string.Concat(prefix, "_", NormalizeEnvironmentName(longName));
                }

                environmentVariableName = environmentVariableName is null
                    ? null
                    : HelpTextSanitizer.SingleLine(environmentVariableName);
            }

            OptionDisplayGroup? displayGroup = option.DisplayGroup;
            bool hasPackGroup = ownedOption.Owner.TryGetPackGroup(option, out OptionPackGroupMetadata packGroup);
            bool isStandardHelp = option.IsHelpOption || option.IsHelpTreeOption;
            string key = isStandardHelp ? "@standard-help" : displayGroup is not null
                ? hasPackGroup ? string.Concat("@pack/", packGroup.InclusionPathKey, "#", displayGroup.Key) : displayGroup.Key
                : hasPackGroup ? packGroup.GroupKey ?? "@ungrouped" : "@ungrouped";
            (CommandDefinition Owner, string Key) groupIdentity = (ownedOption.Owner, key);
            if (!groupsByKey.TryGetValue(groupIdentity, out HelpOptionGroupBuilder? group))
            {
                group = new HelpOptionGroupBuilder(
                    [.. ownedOption.Owner.Path.Select(HelpTextSanitizer.SingleLine)],
                    key,
                    HelpTextSanitizer.SingleLine(isStandardHelp ? "Help options" : displayGroup?.Title ?? packGroup.Title ?? "Options"),
                    HelpTextSanitizer.Description(displayGroup?.Description ?? packGroup.Description),
                    hasPackGroup && (packGroup.GroupKey is not null || displayGroup is not null));
                groupsByKey.Add(groupIdentity, group);
                groups.Add(group);
            }

            group.Options.Add(CreateOptionItem(option, syntax, environmentVariableName));
        }

        string[] visiblePositionals = [.. positionals.Select(static positional => positional.Syntax)];
        bool hasVisibleOptions = groups.Count > 0;
        string[] directiveUsage = directives.Length > 0 ? ["[DIRECTIVES]"] : [];
        string[] optionUsage = hasVisibleOptions ? ["[OPTIONS]"] : [];
        string[] usageParts =
        [
            HelpTextSanitizer.SingleLine(executableName ?? model.Root.Name),
            .. directiveUsage,
            .. selected.Path.Skip(1).Select(HelpTextSanitizer.SingleLine),
            .. optionUsage,
            .. visiblePositionals
        ];
        string usage = string.Join(' ', usageParts);

        return new HelpDocument(
            usage,
            HelpTextSanitizer.Description(selected.Description),
            HelpTextSanitizer.Description(selected.LongDescription),
            children,
            positionals,
            [.. groups.Select(static group => group.Build())],
            directives,
            [.. selected.Examples.Select(example => HelpExampleExpander.Expand(
                example, selected.Path, executableName ?? model.Root.Name))]);
    }

    private static string NormalizeEnvironmentName(string value)
        => value.Replace('-', '_').ToUpperInvariant();

    private static HelpDirectiveItem CreateDirectiveItem(DirectiveDefinition directive)
    {
        DirectiveValueDefinition[] visibleValues = [.. directive.Values
            .Where(static value => !value.IsHidden && !value.IsSensitive)];
        string valueSyntax = visibleValues.Length == 0
            ? directive.Values.Count == 0 ? string.Empty : " <value>"
            : string.Concat(" ", string.Join(' ', visibleValues.Select(static value =>
                value.IsRequired
                    ? $"<{HelpTextSanitizer.SingleLine(value.Name)}>"
                    : $"[{HelpTextSanitizer.SingleLine(value.Name)}]")));
        string syntax = string.Concat("[", HelpTextSanitizer.SingleLine(directive.Name), valueSyntax, "]");
        return new HelpDirectiveItem(
            syntax,
            HelpTextSanitizer.Description(directive.Description));
    }

    private static bool Contains(CommandDefinition root, CommandDefinition target)
    {
        Stack<CommandDefinition> commands = new();
        commands.Push(root);
        while (commands.Count > 0)
        {
            CommandDefinition current = commands.Pop();
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            foreach (CommandDefinition child in current.Children)
            {
                commands.Push(child);
            }
        }

        return false;
    }

    private static HelpPositionalItem CreatePositionalItem(PositionalDefinition positional)
    {
        bool isRest = positional.MaximumValues is null;
        string name = HelpTextSanitizer.SingleLine(positional.Name);
        string shape = isRest ? string.Concat(name, "...") : name;
        string syntax = positional.MinimumValues == 0 ? $"[{shape}]" : $"<{shape}>";
        string description = AppendAllowedValues(HelpTextSanitizer.Description(positional.Description),
            positional.IsSensitive, positional.AllowedValues);
        string? defaultDisplay = CanDisplayDefault(positional)
            ? HelpTextSanitizer.SingleLine(positional.DefaultDisplay!)
            : null;
        return new HelpPositionalItem(syntax, description, positional.MinimumValues > 0, defaultDisplay);
    }

    private static HelpOptionItem CreateOptionItem(
        OptionDefinition option,
        CliSyntaxOptions syntax,
        string? environmentVariableName = null)
    {
        List<string> spellings = [];
        if (option.LongName is string longName)
        {
            spellings.Add(HelpTextSanitizer.SingleLine(syntax.FormatLong(longName)));
        }

        if (option.ShortName is char shortName)
        {
            spellings.Add(HelpTextSanitizer.SingleLine(syntax.FormatShort(shortName)));
        }

        spellings.AddRange(option.Aliases.Select(alias =>
            HelpTextSanitizer.SingleLine(syntax.FormatLong(alias.Name))));
        spellings.AddRange(option.ShortAliases.Select(shortAlias =>
            HelpTextSanitizer.SingleLine(syntax.FormatShort(shortAlias))));
        string valueLabel = HelpTextSanitizer.SingleLine(
            string.IsNullOrEmpty(option.ValueLabel) ? "value" : option.ValueLabel);
        string suffix = option.Arity switch
        {
            OptionArity.BooleanFlag => string.Empty,
            OptionArity.OptionalScalar => $"[={valueLabel}]",
            OptionArity.RequiredScalar or OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection
                => $"<{valueLabel}>",
            OptionArity.Counting => string.Empty,
            _ => throw new InvalidOperationException("The option arity is invalid.")
        };
        string optionSyntax = string.Join(", ", spellings);
        if (suffix.Length > 0)
        {
            optionSyntax = string.Concat(optionSyntax, " ", suffix);
        }

        string? defaultDisplay = CanDisplayDefault(option)
            ? HelpTextSanitizer.SingleLine(option.DefaultDisplay!)
            : null;
        return new HelpOptionItem(
            optionSyntax,
            AppendAllowedValues(HelpTextSanitizer.Description(option.Description), option.IsSensitive, option.AllowedValues),
            option.IsRequired,
            defaultDisplay,
            option.Arity == OptionArity.OptionalScalar,
            environmentVariableName,
            option.Arity is OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection or OptionArity.Counting);
    }

    private static string AppendAllowedValues(string description, bool sensitive, IReadOnlyList<string> values)
    {
        if (sensitive || values.Count == 0)
        {
            return description;
        }

        string choices = string.Join(", ", values.Take(16).Select(static value =>
            HelpTextSanitizer.SingleLine(value.Length > 128 ? value[..128] : value)));
        return string.Concat(description, description.Length == 0 ? string.Empty : " ",
            "(allowed: ", choices, values.Count > 16 ? ", ..." : string.Empty, ")");
    }

    private static bool CanDisplayDefault(OptionDefinition option)
    {
        return option.DefaultDisplay is not null
            && option.DefaultValuePolicy != DefaultValuePolicy.None
            && !option.IsRequired
            && !option.IsSensitive;
    }

    private static bool CanDisplayDefault(PositionalDefinition positional)
    {
        return positional.DefaultDisplay is not null
            && positional.DefaultValuePolicy != DefaultValuePolicy.None
            && positional.MinimumValues == 0
            && !positional.IsSensitive;
    }

    private sealed class HelpOptionGroupBuilder(string[] ownerPath, string key, string title, string description, bool isPack)
    {
        public List<HelpOptionItem> Options { get; } = [];

        public HelpOptionGroup Build() => new(ownerPath, key, title, description, isPack, [.. Options]);
    }
}

/// <summary>A visible directive syntax and sanitized description.</summary>
public sealed class HelpDirectiveItem
{
    internal HelpDirectiveItem(string syntax, string description)
    {
        Syntax = syntax;
        Description = description;
    }

    /// <summary>Gets the bracketed directive syntax.</summary>
    public string Syntax { get; }

    /// <summary>Gets the sanitized description.</summary>
    public string Description { get; }
}

/// <summary>A visible child command summary in a help document.</summary>
public sealed class HelpCommandItem
{
    private readonly ReadOnlyCollection<string> _aliases;

    internal HelpCommandItem(string name, string[] aliases, string description)
    {
        Name = name;
        _aliases = Array.AsReadOnly(aliases);
        Description = description;
    }

    /// <summary>Gets the canonical command name.</summary>
    public string Name { get; }

    /// <summary>Gets the ordered aliases.</summary>
    public IReadOnlyList<string> Aliases => _aliases;

    /// <summary>Gets the sanitized description.</summary>
    public string Description { get; }
}

/// <summary>A visible positional summary in a help document.</summary>
public sealed class HelpPositionalItem
{
    internal HelpPositionalItem(string syntax, string description, bool isRequired, string? defaultDisplay)
    {
        Syntax = syntax;
        Description = description;
        IsRequired = isRequired;
        DefaultDisplay = defaultDisplay;
    }

    /// <summary>Gets the usage placeholder.</summary>
    public string Syntax { get; }

    /// <summary>Gets the sanitized description.</summary>
    public string Description { get; }

    /// <summary>Gets whether at least one value is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets explicitly permitted default display text, when safe.</summary>
    public string? DefaultDisplay { get; }
}

/// <summary>A visible option summary in a help document.</summary>
public sealed class HelpOptionItem
{
    internal HelpOptionItem(
        string syntax,
        string description,
        bool isRequired,
        string? defaultDisplay,
        bool requiresEqualsForOptionalValue,
        string? environmentVariableName,
        bool isRepeatable)
    {
        Syntax = syntax;
        Description = description;
        IsRequired = isRequired;
        DefaultDisplay = defaultDisplay;
        RequiresEqualsForOptionalValue = requiresEqualsForOptionalValue;
        EnvironmentVariableName = environmentVariableName;
        IsRepeatable = isRepeatable;
    }

    /// <summary>Gets the canonical option syntax and aliases.</summary>
    public string Syntax { get; }

    /// <summary>Gets the sanitized description.</summary>
    public string Description { get; }

    /// <summary>Gets whether an occurrence is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets explicitly permitted default display text, when safe.</summary>
    public string? DefaultDisplay { get; }

    /// <summary>Gets whether an optional value must use equals syntax.</summary>
    public bool RequiresEqualsForOptionalValue { get; }

    /// <summary>Gets the eligible environment-variable name, when host processing is enabled.</summary>
    public string? EnvironmentVariableName { get; }

    /// <summary>Gets whether the option can occur multiple times.</summary>
    public bool IsRepeatable { get; }
}

/// <summary>A stable help group with ordered visible options.</summary>
public sealed class HelpOptionGroup
{
    private readonly ReadOnlyCollection<string> _ownerPath;
    private readonly ReadOnlyCollection<HelpOptionItem> _options;

    internal HelpOptionGroup(
        string[] ownerPath,
        string key,
        string title,
        string description,
        bool isPack,
        HelpOptionItem[] options)
    {
        _ownerPath = Array.AsReadOnly(ownerPath);
        Key = key;
        Title = title;
        Description = description;
        IsPack = isPack;
        _options = Array.AsReadOnly(options);
    }

    /// <summary>Gets the stable group key, independent of its display title.</summary>
    public string Key { get; }

    /// <summary>Gets the sanitized canonical command path that owns the group.</summary>
    public IReadOnlyList<string> OwnerPath => _ownerPath;

    /// <summary>Gets the sanitized display title.</summary>
    public string Title { get; }

    /// <summary>Gets the sanitized description.</summary>
    public string Description { get; }

    /// <summary>Gets whether this group represents a titled option pack.</summary>
    public bool IsPack { get; }

    /// <summary>Gets visible options in definition order.</summary>
    public IReadOnlyList<HelpOptionItem> Options => _options;
}

internal static class HelpTextSanitizer
{
    public static string SingleLine(string value)
    {
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            if (character is '\r' or '\n' or '\t')
            {
                if (builder.Length == 0 || builder[^1] != ' ')
                {
                    _ = builder.Append(' ');
                }
            }
            else if (!char.IsControl(character) && !IsBidiControl(character) && character is not ('\u2028' or '\u2029'))
            {
                _ = builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string Description(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is '\r')
            {
                if (index + 1 < value.Length && value[index + 1] == '\n')
                {
                    index++;
                }

                _ = builder.Append('\n');
            }
            else if (character == '\n')
            {
                _ = builder.Append(character);
            }
            else if (!char.IsControl(character) && !IsBidiControl(character) && character is not ('\u2028' or '\u2029'))
            {
                _ = builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static string? DescriptionOrNull(string? value) => value is null ? null : Description(value);

    private static bool IsBidiControl(char character)
    {
        return character is '\u061c' or '\u200e' or '\u200f'
            or (>= '\u202a' and <= '\u202e')
            or (>= '\u2066' and <= '\u206f');
    }
}
