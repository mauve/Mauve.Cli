using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>An immutable, validated snapshot of a command graph.</summary>
public sealed class CommandModel
{
    private CommandModel(CommandDefinition root, CliLimits limits, bool hasStandardHelp)
    {
        Root = root;
        Limits = limits;
        HasStandardHelp = hasStandardHelp;
    }

    /// <summary>Gets the root command definition.</summary>
    public CommandDefinition Root { get; }

    /// <summary>Gets the immutable limits used to validate and parse this model.</summary>
    public CliLimits Limits { get; }

    /// <summary>Gets whether standard help was explicitly installed in this model.</summary>
    public bool HasStandardHelp { get; }

    /// <summary>Creates and validates an immutable snapshot from a command graph.</summary>
    /// <param name="root">The root command builder.</param>
    /// <param name="limits">Optional positive model and parsing limits.</param>
    /// <param name="includeStandardHelp">Whether to install <c>--help</c>/<c>-h</c> on each command.</param>
    /// <returns>A validated command model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    /// <exception cref="ArgumentException">The graph contains invalid or ambiguous definitions.</exception>
    public static CommandModel Create(
        CommandBuilder root,
        CliLimits? limits = null,
        bool includeStandardHelp = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        CliLimits effectiveLimits = limits ?? CliLimits.Default;

        Dictionary<CommandBuilder, NodeSnapshot> snapshots = [with(ReferenceEqualityComparer.Instance)];
        Dictionary<CommandBuilder, string[]> paths = [with(ReferenceEqualityComparer.Instance)];
        Dictionary<CommandBuilder, CommandDefinition> definitions = [with(ReferenceEqualityComparer.Instance)];
        HashSet<CommandBuilder> active = [with(ReferenceEqualityComparer.Instance)];
        HashSet<CommandBuilder> visited = [with(ReferenceEqualityComparer.Instance)];
        Stack<TraversalFrame> traversal = new();
        traversal.Push(new TraversalFrame(root, 1, false));
        paths.Add(root, [root.Name]);

        long modelNodeCount = 0;
        while (traversal.Count > 0)
        {
            TraversalFrame frame = traversal.Pop();
            if (frame.IsExit)
            {
                _ = active.Remove(frame.Builder);
                NodeSnapshot snapshot = snapshots[frame.Builder];
                CommandDefinition[] children = [.. snapshot.Children.Select(child => definitions[child])];
                definitions.Add(frame.Builder, new CommandDefinition(
                    frame.Builder,
                    snapshot.Aliases,
                    children,
                    snapshot.Options,
                    snapshot.Positionals,
                    snapshot.CrossValidators,
                    snapshot.Handler,
                    paths[frame.Builder]));
                continue;
            }

            if (active.Contains(frame.Builder))
            {
                throw InvalidModel("The command graph contains a cycle.");
            }

            if (!visited.Add(frame.Builder))
            {
                throw InvalidModel("A command definition is attached under more than one parent.");
            }

            if (frame.Depth > effectiveLimits.MaximumCommandDepth)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The command graph exceeds the maximum depth of {effectiveLimits.MaximumCommandDepth}."));
            }

            int addedHelpOption = includeStandardHelp ? 1 : 0;
            if (frame.Builder.OptionCount + addedHelpOption > effectiveLimits.MaximumOptionsPerCommand)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Command '{frame.Builder.Name}' exceeds the maximum of {effectiveLimits.MaximumOptionsPerCommand} options."));
            }

            long minimumRemainingNodes = modelNodeCount
                + 1
                + frame.Builder.OptionCount
                + addedHelpOption
                + frame.Builder.PositionalCount
                + frame.Builder.ChildCount;
            if (minimumRemainingNodes > effectiveLimits.MaximumModelNodes)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The command model exceeds the maximum of {effectiveLimits.MaximumModelNodes} nodes."));
            }

            CommandBuilder[] childrenSnapshot = frame.Builder.CopyChildren();
            string[] aliases = frame.Builder.CopyAliases();
            OptionDefinition[] options = includeStandardHelp
                ? [.. frame.Builder.CopyOptions(), OptionDefinition.CreateStandardHelp()]
                : frame.Builder.CopyOptions();
            PositionalDefinition[] positionals = frame.Builder.CopyPositionals();
            CrossValueValidator[] crossValidators = frame.Builder.CopyCrossValidators();
            modelNodeCount += 1L + options.Length + positionals.Length;

            ValidateCommand(frame.Builder, aliases, options, positionals, frame.Depth == 1);
            ValidateChildren(childrenSnapshot);
            snapshots.Add(frame.Builder, new NodeSnapshot(
                aliases,
                childrenSnapshot,
                options,
                positionals,
                crossValidators,
                frame.Builder.Handler));
            _ = active.Add(frame.Builder);
            traversal.Push(new TraversalFrame(frame.Builder, frame.Depth, true));

            for (int index = childrenSnapshot.Length - 1; index >= 0; index--)
            {
                CommandBuilder child = childrenSnapshot[index];
                string[] parentPath = paths[frame.Builder];
                string[] childPath = new string[parentPath.Length + 1];
                Array.Copy(parentPath, childPath, parentPath.Length);
                childPath[^1] = child.Name;
                if (!paths.TryAdd(child, childPath))
                {
                    if (active.Contains(child))
                    {
                        throw InvalidModel("The command graph contains a cycle.");
                    }

                    throw InvalidModel("A command definition is attached under more than one parent.");
                }

                traversal.Push(new TraversalFrame(child, frame.Depth + 1, false));
            }
        }

        return new CommandModel(definitions[root], effectiveLimits, includeStandardHelp);
    }

    private static void ValidateCommand(
        CommandBuilder command,
        string[] aliases,
        OptionDefinition[] options,
        PositionalDefinition[] positionals,
        bool isRoot)
    {
        if (isRoot)
        {
            ValidateRootName(command.Name);
        }
        else
        {
            ValidateCommandName(command.Name);
        }

        HashSet<string> commandNames =
        [
with(StringComparer.Ordinal),             command.Name
        ];
        foreach (string alias in aliases)
        {
            ValidateCommandName(alias);
            if (!commandNames.Add(alias))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' repeats the name or alias '{alias}'."));
            }
        }

        HashSet<string> optionNames = [with(StringComparer.Ordinal)];
        Dictionary<string, OptionDisplayGroup> displayGroups = new(StringComparer.Ordinal);
        foreach (OptionDefinition option in options)
        {
            ValidateOption(option);
            if (!optionNames.Add(option.Name))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate option spelling '{option.Name}'."));
            }

            foreach (OptionAlias alias in option.Aliases)
            {
                if (!optionNames.Add(alias.Name))
                {
                    throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate option spelling '{alias.Name}'."));
                }
            }

            if (option.DisplayGroup is OptionDisplayGroup displayGroup)
            {
                if (displayGroups.TryGetValue(displayGroup.Key, out OptionDisplayGroup? existingGroup)
                    && (!string.Equals(existingGroup.Title, displayGroup.Title, StringComparison.Ordinal)
                        || !string.Equals(existingGroup.Description, displayGroup.Description, StringComparison.Ordinal)))
                {
                    throw InvalidModel(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Command '{command.Name}' declares conflicting metadata for display group '{displayGroup.Key}'."));
                }

                _ = displayGroups.TryAdd(displayGroup.Key, displayGroup);
            }
        }

        HashSet<string> positionalNames = [with(StringComparer.Ordinal)];
        bool optionalSeen = false;
        for (int index = 0; index < positionals.Length; index++)
        {
            PositionalDefinition positional = positionals[index];
            ValidatePositional(positional);
            if (!positionalNames.Add(positional.Name))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate positional name '{positional.Name}'."));
            }

            if (optionalSeen && positional.MinimumValues > 0)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Required positional '{positional.Name}' cannot follow an optional positional."));
            }

            if (positional.MinimumValues == 0)
            {
                optionalSeen = true;
            }

            if (positional.MaximumValues is null && index != positionals.Length - 1)
            {
                throw InvalidModel("A rest positional must be the final positional definition.");
            }
        }
    }

    private static void ValidateChildren(CommandBuilder[] children)
    {
        HashSet<string> names = [with(StringComparer.Ordinal)];
        foreach (CommandBuilder child in children)
        {
            ValidateCommandName(child.Name);
            if (!names.Add(child.Name))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Sibling commands share the name or alias '{child.Name}'."));
            }

            foreach (string alias in child.CopyAliases())
            {
                ValidateCommandName(alias);
                if (!names.Add(alias))
                {
                    throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Sibling commands share the name or alias '{alias}'."));
                }
            }
        }
    }

    private static void ValidateOption(OptionDefinition option)
    {
        ValidateLongOptionName(option.Name);
        if (!Enum.IsDefined(option.Arity))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' has an unsupported arity."));
        }

        if (option.AllowsNull && !CanRepresentNull(option.ValueType))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' allows null but its value type cannot represent null."));
        }

        ValidateDefaultValue(
            option.Name,
            option.ValueType,
            option.AllowsNull,
            option.DefaultValuePolicy,
            option.DefaultValue,
            option.DefaultValueFactory);
        foreach (IValueValidator validator in option.Validators)
        {
            if (validator.ValueType != option.ValueType)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Validator for option '{option.Name}' has the wrong value type."));
            }
        }

        HashSet<string> names =
        [
with(StringComparer.Ordinal),             option.Name
        ];
        bool hasNegatingAlias = false;
        foreach (OptionAlias alias in option.Aliases)
        {
            ValidateOptionAlias(alias.Name);
            if (!names.Add(alias.Name))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' repeats the spelling '{alias.Name}'."));
            }

            if (alias.Kind == OptionAliasKind.Negating)
            {
                hasNegatingAlias = true;
            }
            else if (alias.Kind != OptionAliasKind.Normal)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' has an unsupported alias kind."));
            }
        }

        if (hasNegatingAlias && (option.Arity != OptionArity.BooleanFlag || option.ValueType != typeof(bool)))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Negating aliases require a boolean flag: '{option.Name}'."));
        }

        if (option.Arity == OptionArity.BooleanFlag && option.ValueType != typeof(bool))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Boolean flag '{option.Name}' must have a Boolean value type."));
        }

        if (option.Arity == OptionArity.BooleanFlag && option.DefaultValuePolicy != DefaultValuePolicy.None)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Boolean flag '{option.Name}' has an intrinsic absent value and cannot declare another default."));
        }

        bool isCollection = option.Arity is OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection;
        if (isCollection)
        {
            if (option.ElementType is null || !HasCollectionValueType(option.ValueType, option.ElementType))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Collection option '{option.Name}' must use IReadOnlyList<T> matching its element type."));
            }
        }
        else if (option.ElementType is not null)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Scalar option '{option.Name}' cannot declare a collection element type."));
        }

        if (option.Arity == OptionArity.OptionalScalar)
        {
            if (!Enum.IsDefined(option.BareValuePolicy) || option.BareValuePolicy == BareValuePolicy.Unspecified)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' must declare bare-value behavior."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Null && !option.AllowsNull)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' can use a null bare value only when null is allowed."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Constant && option.BareValue is null && !option.AllowsNull)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' has a null bare constant but does not allow null."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Constant
                && option.BareValue is not null
                && !IsCompatibleValue(option.ValueType, option.BareValue))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' has a bare constant of the wrong type."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Constant
                && option.BareValue is not null
                && !IsImmutableDefault(option.BareValue))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' must use a factory for mutable bare values."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Factory && option.BareValueFactory is null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' must provide a bare-value factory."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Factory && option.BareValue is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' cannot declare both a bare factory and constant."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Constant && option.BareValueFactory is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' has a factory with a constant bare-value policy."));
            }

            if (option.BareValuePolicy != BareValuePolicy.Factory && option.BareValueFactory is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' has a factory with a different bare-value policy."));
            }

            if (option.BareValuePolicy == BareValuePolicy.Null && option.BareValue is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Optional option '{option.Name}' has a value with a null bare-value policy."));
            }
        }
        else if (option.BareValuePolicy != BareValuePolicy.Unspecified
            || option.BareValue is not null
            || option.BareValueFactory is not null)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Only optional option '{option.Name}' can declare bare-value behavior."));
        }
    }

    private static void ValidatePositional(PositionalDefinition positional)
    {
        ValidateCommandName(positional.Name);
        if (positional.AllowsNull && !CanRepresentNull(positional.ValueType))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Positional '{positional.Name}' allows null but its value type cannot represent null."));
        }

        ValidateDefaultValue(
            positional.Name,
            positional.ValueType,
            positional.AllowsNull,
            positional.DefaultValuePolicy,
            positional.DefaultValue,
            positional.DefaultValueFactory);
        foreach (IValueValidator validator in positional.Validators)
        {
            if (validator.ValueType != positional.ValueType)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Validator for positional '{positional.Name}' has the wrong value type."));
            }
        }

        if (positional.MinimumValues is < 0 or > 1)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Positional '{positional.Name}' must have a minimum arity of zero or one."));
        }

        if (positional.MaximumValues is int maximum)
        {
            if (maximum != 1 || positional.MinimumValues > maximum)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Positional '{positional.Name}' must have scalar arity zero-or-one or one."));
            }

            if (positional.ElementType is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Scalar positional '{positional.Name}' cannot declare an element type."));
            }
        }
        else if (positional.ElementType is null || !HasCollectionValueType(positional.ValueType, positional.ElementType))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Rest positional '{positional.Name}' must use IReadOnlyList<T> matching its element type."));
        }

        if (positional.MaximumValues is null && positional.DefaultValuePolicy != DefaultValuePolicy.None)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Rest positional '{positional.Name}' uses its built-in empty-sequence default."));
        }
    }

    private static bool HasCollectionValueType(Type valueType, Type elementType)
    {
        return valueType.IsGenericType
            && valueType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            && valueType.GetGenericArguments()[0] == elementType;
    }

    private static bool CanRepresentNull(Type valueType)
    {
        return !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;
    }

    private static void ValidateDefaultValue(
        string name,
        Type valueType,
        bool allowsNull,
        DefaultValuePolicy policy,
        object? value,
        Func<object?>? factory)
    {
        if (!Enum.IsDefined(policy))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Definition '{name}' has an unsupported default policy."));
        }

        if (policy == DefaultValuePolicy.None)
        {
            if (value is not null || factory is not null)
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Definition '{name}' declares a default with no default policy."));
            }

            return;
        }

        if (policy == DefaultValuePolicy.Constant)
        {
            if (factory is not null
                || (value is null && !allowsNull)
                || (value is not null && !IsCompatibleValue(valueType, value)))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Definition '{name}' has an invalid default constant."));
            }

            if (value is not null && !IsImmutableDefault(value))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Definition '{name}' must use a factory for mutable reference defaults."));
            }

            return;
        }

        if (value is not null || factory is null)
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Definition '{name}' must declare exactly one default factory."));
        }
    }

    private static bool IsImmutableDefault(object value)
    {
        return value.GetType().IsValueType || value is string or Uri;
    }

    private static bool IsCompatibleValue(Type valueType, object value)
    {
        return valueType.IsInstanceOfType(value)
            || Nullable.GetUnderlyingType(valueType)?.IsInstanceOfType(value) == true;
    }

    private static void ValidateRootName(string name)
    {
        if (name.Length is 0 or > 128 || name.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw InvalidModel("The root display label must be nonempty, at most 128 UTF-16 code units, and contain no whitespace or control characters.");
        }
    }

    private static void ValidateCommandName(string name)
    {
        if (name.Length is 0 or > 128 || !IsAsciiAlphaNumeric(name[0]))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command or positional name '{name}' is invalid."));
        }

        for (int index = 1; index < name.Length; index++)
        {
            char character = name[index];
            if (!IsAsciiAlphaNumeric(character) && character != '-')
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command or positional name '{name}' is invalid."));
            }
        }
    }

    private static void ValidateLongOptionName(string name)
    {
        if (name.Length is < 3 or > 128 || !name.StartsWith("--", StringComparison.Ordinal) || !IsAsciiLetter(name[2]))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Canonical option name '{name}' is invalid."));
        }

        for (int index = 3; index < name.Length; index++)
        {
            char character = name[index];
            if (!IsAsciiAlphaNumeric(character) && character != '-')
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Canonical option name '{name}' is invalid."));
            }
        }
    }

    private static void ValidateOptionAlias(string name)
    {
        if (name.Length == 2 && name[0] == '-' && IsAsciiLetter(name[1]))
        {
            return;
        }

        ValidateLongOptionName(name);
    }

    private static bool IsAsciiAlphaNumeric(char character)
    {
        return IsAsciiLetter(character) || character is >= '0' and <= '9';
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    private static ArgumentException InvalidModel(string message)
    {
        return new ArgumentException(message);
    }

    private sealed record NodeSnapshot(
        string[] Aliases,
        CommandBuilder[] Children,
        OptionDefinition[] Options,
        PositionalDefinition[] Positionals,
        CrossValueValidator[] CrossValidators,
        CommandHandler? Handler);

    private readonly record struct TraversalFrame(CommandBuilder Builder, int Depth, bool IsExit);
}
