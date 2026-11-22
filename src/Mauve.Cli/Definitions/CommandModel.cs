using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mauve.Cli.Conversion;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Definitions;

/// <summary>An immutable, validated snapshot of a command graph.</summary>
public sealed class CommandModel
{
    private CommandModel(
        CommandDefinition root,
        CliLimits limits,
        bool hasStandardHelp,
        DirectiveDefinition[] directives,
        ConverterRegistry converters)
    {
        Root = root;
        Limits = limits;
        HasStandardHelp = hasStandardHelp;
        Directives = Array.AsReadOnly(directives);
        Converters = converters;
    }

    /// <summary>Gets the root command definition.</summary>
    public CommandDefinition Root { get; }

    /// <summary>Gets the immutable limits used to validate and parse this model.</summary>
    public CliLimits Limits { get; }

    /// <summary>Gets whether standard help was explicitly installed in this model.</summary>
    public bool HasStandardHelp { get; }

    /// <summary>Gets directives registered on the root, in declaration order.</summary>
    public IReadOnlyList<DirectiveDefinition> Directives { get; }

    /// <summary>Gets the immutable model-wide converter registry.</summary>
    public ConverterRegistry Converters { get; }

    /// <summary>Creates and validates an immutable snapshot from a command graph.</summary>
    /// <param name="root">The root command builder.</param>
    /// <param name="limits">Optional positive model and parsing limits.</param>
    /// <param name="includeStandardHelp">Whether to install <c>--help</c>/<c>-h</c> and <c>--help-tree</c> on each command.</param>
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
                    paths[frame.Builder],
                    snapshot.Packs,
                    snapshot.PackGroups));
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

            int addedHelpOption = includeStandardHelp ? 2 : 0;
            DirectiveBuilder[] directiveBuilders = frame.Builder.CopyDirectives();
            if (frame.Depth != 1 && directiveBuilders.Length > 0)
            {
                throw InvalidModel("Directives can be registered only on the command model root.");
            }

            if (frame.Depth != 1 && frame.Builder.HasConverters)
            {
                throw InvalidModel("Model-wide converters can be registered only on the command root.");
            }

            OptionPackSnapshot[] packs = SnapshotPacks(
                frame.Builder.CopyPacks(),
                effectiveLimits,
                out OptionDefinition[] packedOptions,
                out Dictionary<OptionDefinition, OptionPackGroupMetadata> packGroups,
                out int packNodeCount);
            int optionCount = frame.Builder.OptionCount + packedOptions.Length + addedHelpOption;
            if (optionCount > effectiveLimits.MaximumOptionsPerCommand)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Command '{frame.Builder.Name}' exceeds the maximum of {effectiveLimits.MaximumOptionsPerCommand} options."));
            }

            long minimumRemainingNodes = modelNodeCount
                + 1
                + optionCount
                + (frame.Depth == 1
                    ? directiveBuilders.Sum(static directive => 1L + directive.ValueCount)
                    : 0)
                + packNodeCount
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
            OptionDefinition[] directOptions = frame.Builder.CopyOptions();
            OptionDefinition[] options = includeStandardHelp
                ? [.. directOptions, .. packedOptions, OptionDefinition.CreateStandardHelp(), OptionDefinition.CreateStandardHelpTree()]
                : [.. directOptions, .. packedOptions];
            PositionalDefinition[] positionals = frame.Builder.CopyPositionals();
            CrossValueValidator[] crossValidators = frame.Builder.CopyCrossValidators();
            modelNodeCount += 1L + options.Length + positionals.Length + packNodeCount;
            if (frame.Depth == 1)
            {
                modelNodeCount += directiveBuilders.Sum(static directive => 1L + directive.ValueCount);
            }

            ValidateCommand(frame.Builder, aliases, options, positionals, packGroups, frame.Depth == 1);
            ValidateChildren(childrenSnapshot);
            snapshots.Add(frame.Builder, new NodeSnapshot(
                aliases,
                childrenSnapshot,
                options,
                positionals,
                crossValidators,
                frame.Builder.Handler,
                packs,
                packGroups));
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

        CommandDefinition rootDefinition = definitions[root];
        ValidateEffectiveOptions(rootDefinition, effectiveLimits);
        DirectiveDefinition[] directives = [.. root.CopyDirectives().Select(static directive => directive.Build())];
        ValidateDirectives(directives);
        return new CommandModel(rootDefinition, effectiveLimits, includeStandardHelp, directives, root.CopyConverters());
    }

    private static void ValidateDirectives(DirectiveDefinition[] directives)
    {
        HashSet<string> directiveNames = [with(StringComparer.Ordinal)];
        for (int directiveIndex = 0; directiveIndex < directives.Length; directiveIndex++)
        {
            DirectiveDefinition directive = directives[directiveIndex];
            if (!IsValidDirectiveName(directive.Name) || !directiveNames.Add(directive.Name))
            {
                throw InvalidModel("Directive names must be valid and unique on the root.");
            }

            HashSet<string> valueNames = [with(StringComparer.Ordinal)];
            bool collectionSeen = false;
            for (int valueIndex = 0; valueIndex < directive.Values.Count; valueIndex++)
            {
                DirectiveValueDefinition value = directive.Values[valueIndex];
                ValidateConverter(value.Converter, value.ElementType ?? value.ValueType);
                if (!IsValidDirectiveName(value.Name) || !valueNames.Add(value.Name))
                {
                    throw InvalidModel(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Directive '{directive.Name}' has an invalid or duplicate value name."));
                }

                if (value.ElementType is not null)
                {
                    if (collectionSeen || valueIndex != directive.Values.Count - 1)
                    {
                        throw InvalidModel("A directive collection value must be the final value definition.");
                    }

                    collectionSeen = true;
                }

            }

            if (directive.Handler is null)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Directive '{directive.Name}' has no handler."));
            }
        }
    }

    private static bool IsValidDirectiveName(string name)
    {
        return name.Length is > 0 and <= 128
            && (name[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            && name.All(static character =>
                character is (>= 'A' and <= 'Z')
                    or (>= 'a' and <= 'z')
                    or (>= '0' and <= '9')
                    or '-');
    }

    internal static IReadOnlyList<CommandOption> GetEffectiveOptions(
        IReadOnlyList<CommandDefinition> selectedPath)
    {
        List<CommandOption> options = [];
        for (int pathIndex = selectedPath.Count - 1; pathIndex >= 0; pathIndex--)
        {
            CommandDefinition owner = selectedPath[pathIndex];
            foreach (OptionDefinition option in owner.Options)
            {
                if (pathIndex == selectedPath.Count - 1 || option.Scope == OptionScope.Inherited)
                {
                    options.Add(new CommandOption(owner, option));
                }
            }
        }

        return options;
    }

    private static OptionPackSnapshot[] SnapshotPacks(
        OptionPackInclusionBuilder[] inclusions,
        CliLimits limits,
        out OptionDefinition[] options,
        out Dictionary<OptionDefinition, OptionPackGroupMetadata> groups,
        out int packNodeCount)
    {
        List<OptionDefinition> flattenedOptions = [];
        groups = [with(ReferenceEqualityComparer.Instance)];
        HashSet<OptionPackBuilder> active = [with(ReferenceEqualityComparer.Instance)];
        packNodeCount = 0;
        OptionPackSnapshot[] snapshots = SnapshotPackInclusions(
            inclusions,
            limits,
            active,
            [],
            null,
            1,
            flattenedOptions,
            groups,
            ref packNodeCount);
        options = [.. flattenedOptions];
        return snapshots;
    }

    private static OptionPackSnapshot[] SnapshotPackInclusions(
        OptionPackInclusionBuilder[] inclusions,
        CliLimits limits,
        HashSet<OptionPackBuilder> active,
        string[] parentPath,
        OptionPackGroupMetadata? inheritedGroup,
        int depth,
        List<OptionDefinition> flattenedOptions,
        Dictionary<OptionDefinition, OptionPackGroupMetadata> groups,
        ref int packNodeCount)
    {
        if (inclusions.Length == 0)
        {
            return [];
        }

        if (depth > limits.MaximumCommandDepth)
        {
            throw InvalidModel("The option-pack graph exceeds the configured nesting limit.");
        }

        HashSet<string> siblingKeys = [with(StringComparer.Ordinal)];
        List<OptionPackSnapshot> snapshots = [];
        foreach (OptionPackInclusionBuilder inclusion in inclusions)
        {
            ValidatePackKey(inclusion.Key);
            if (!siblingKeys.Add(inclusion.Key))
            {
                throw InvalidModel("Sibling option-pack inclusion keys must be unique.");
            }

            if (!active.Add(inclusion.Pack))
            {
                throw InvalidModel("The option-pack graph contains a cycle.");
            }

            packNodeCount++;
            long minimumPackNodes = (long)flattenedOptions.Count
                + packNodeCount
                + inclusion.Pack.OptionCount
                + inclusion.Pack.PackCount;
            if (minimumPackNodes > limits.MaximumModelNodes)
            {
                throw InvalidModel("The option-pack graph exceeds the configured model-node limit.");
            }

            string[] inclusionPath = [.. parentPath, inclusion.Key];
            string inclusionPathKey = string.Join('/', inclusionPath);
            string groupPathKey = string.Concat("@pack/", inclusionPathKey);
            OptionPackGroupMetadata? currentGroup = inclusion.Title is not null
                ? new OptionPackGroupMetadata(
                    inclusionPathKey,
                    groupPathKey,
                    inclusion.Title,
                    inclusion.Description)
                : inheritedGroup;
            OptionPackSnapshot[] nestedPacks = SnapshotPackInclusions(
                inclusion.Pack.CopyPacks(),
                limits,
                active,
                inclusionPath,
                currentGroup,
                depth + 1,
                flattenedOptions,
                groups,
                ref packNodeCount);
            OptionDefinition[] ownOptions = inclusion.Pack.CopyOptions();
            foreach (OptionDefinition option in ownOptions)
            {
                flattenedOptions.Add(option);
                if (!groups.TryAdd(
                    option,
                    new OptionPackGroupMetadata(
                        inclusionPathKey,
                        currentGroup?.GroupKey,
                        currentGroup?.Title,
                        currentGroup?.Description)))
                {
                    throw InvalidModel("An option definition cannot be included more than once in one command.");
                }
            }

            _ = active.Remove(inclusion.Pack);
            snapshots.Add(new OptionPackSnapshot(
                inclusion.Key,
                ownOptions,
                nestedPacks,
                inclusion.Title,
                inclusion.Description));
        }

        return [.. snapshots];
    }

    private static void ValidatePackKey(string key)
    {
        if (key.Length is 0 or > 128
            || key.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw InvalidModel("An option-pack inclusion key must be 1 to 128 ASCII letters, digits, dots, underscores or hyphens.");
        }
    }

    internal IReadOnlyList<CommandDefinition> GetPath(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        List<CommandDefinition> path = [Root];
        CommandDefinition current = Root;
        for (int index = 1; index < command.Path.Count; index++)
        {
            string component = command.Path[index];
            CommandDefinition? child = current.Children.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, component, StringComparison.Ordinal)) ?? throw new ArgumentException("The command does not belong to this model.", nameof(command));
            path.Add(child);
            current = child;
        }

        return !ReferenceEquals(current, command)
            ? throw new ArgumentException("The command does not belong to this model.", nameof(command))
            : path;
    }

    private static void ValidateEffectiveOptions(CommandDefinition root, CliLimits limits)
    {
        Stack<(CommandDefinition Command, CommandDefinition[] Path)> pending = new();
        pending.Push((root, [root]));
        while (pending.Count > 0)
        {
            (CommandDefinition command, CommandDefinition[] path) = pending.Pop();
            IReadOnlyList<CommandOption> effective = GetEffectiveOptions(path);
            HashSet<string> longNames = [with(StringComparer.Ordinal)];
            HashSet<char> shortNames = [];
            foreach (CommandOption option in effective)
            {
                if (option.Definition.LongName is string longName && !longNames.Add(longName))
                {
                    throw InvalidModel(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The effective command path contains a duplicate option spelling '{longName}'."));
                }

                foreach (OptionAlias alias in option.Definition.Aliases)
                {
                    if (!longNames.Add(alias.Name))
                    {
                        throw InvalidModel(string.Create(
                            CultureInfo.InvariantCulture,
                            $"The effective command path contains a duplicate option spelling '{alias.Name}'."));
                    }
                }

                if (option.Definition.ShortName is char shortName && !shortNames.Add(shortName))
                {
                    throw InvalidModel("The effective command path contains a duplicate short option character.");
                }

                foreach (char shortAlias in option.Definition.ShortAliases)
                {
                    if (!shortNames.Add(shortAlias))
                    {
                        throw InvalidModel("The effective command path contains a duplicate short option character.");
                    }
                }
            }

            if (effective.Count > limits.MaximumOptionsPerCommand)
            {
                throw InvalidModel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The effective command path exceeds the maximum of {limits.MaximumOptionsPerCommand} options."));
            }

            for (int index = command.Children.Count - 1; index >= 0; index--)
            {
                CommandDefinition child = command.Children[index];
                CommandDefinition[] childPath = new CommandDefinition[path.Length + 1];
                Array.Copy(path, childPath, path.Length);
                childPath[^1] = child;
                pending.Push((child, childPath));
            }
        }
    }

    private static void ValidateCommand(
        CommandBuilder command,
        string[] aliases,
        OptionDefinition[] options,
        PositionalDefinition[] positionals,
        Dictionary<OptionDefinition, OptionPackGroupMetadata> packGroups,
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
        HashSet<char> shortOptionNames = [];
        Dictionary<string, (string? Title, string? Description)> displayGroups = [with(StringComparer.Ordinal)];
        foreach (OptionDefinition option in options)
        {
            ValidateOption(option);
            if (option.LongName is string longName && !optionNames.Add(longName))
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate option spelling '{longName}'."));
            }

            foreach (OptionAlias alias in option.Aliases)
            {
                if (!optionNames.Add(alias.Name))
                {
                    throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate option spelling '{alias.Name}'."));
                }

                if (option.ShortName is char shortName && !shortOptionNames.Add(shortName))
                {
                    throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate short option character."));
                }

                foreach (char shortAlias in option.ShortAliases)
                {
                    if (!shortOptionNames.Add(shortAlias))
                    {
                        throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Command '{command.Name}' has a duplicate short option character."));
                    }
                }
            }

            bool hasPackGroup = packGroups.TryGetValue(option, out OptionPackGroupMetadata packGroup);
            string? groupKey = option.DisplayGroup is OptionDisplayGroup explicitGroup
                ? hasPackGroup ? string.Concat("@pack/", packGroup.InclusionPathKey, "#", explicitGroup.Key) : explicitGroup.Key
                : packGroup.GroupKey;
            string? groupTitle = option.DisplayGroup?.Title ?? packGroup.Title;
            string? groupDescription = option.DisplayGroup?.Description ?? packGroup.Description;
            if (groupKey is not null)
            {
                if (displayGroups.TryGetValue(groupKey, out (string? Title, string? Description) existingGroup)
                    && (!string.Equals(existingGroup.Title, groupTitle, StringComparison.Ordinal)
                        || !string.Equals(existingGroup.Description, groupDescription, StringComparison.Ordinal)))
                {
                    throw InvalidModel(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Command '{command.Name}' declares conflicting metadata for a help group."));
                }

                _ = displayGroups.TryAdd(groupKey, (groupTitle, groupDescription));
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
        ValidateConverter(option.Converter, option.ElementType ?? option.ValueType);
        if (option.Arity == OptionArity.Counting
            && ((option.ValueType != typeof(int)
                && option.ValueType != typeof(int?))
                || option.Converter is not null
                || option.DefaultValuePolicy != DefaultValuePolicy.None
                || option.BareValuePolicy != BareValuePolicy.Unspecified))
        {
            throw InvalidModel($"Counting option '{option.Name}' must be an int or nullable int without converters or explicit defaults.");
        }

        if (option.Arity == OptionArity.BooleanFlag && option.Converter is not null)
        {
            throw InvalidModel("Boolean flags use intrinsic literal parsing and cannot declare a converter.");
        }
        if (option.LongName is string longName)
        {
            ValidateLongOptionName(longName);
        }

        if (option.LongName is null && option.ShortName is null && option.ShortAliases.Count == 0)
        {
            throw InvalidModel("An option must declare at least one long or short spelling.");
        }

        if (option.ShortName is char primaryShort && !IsAsciiLetter(primaryShort))
        {
            throw InvalidModel($"Option '{option.Name}' has an invalid short name.");
        }

        HashSet<char> shortCharacters = [];
        if (option.ShortName is char declaredShort)
        {
            _ = shortCharacters.Add(declaredShort);
        }

        foreach (char shortAlias in option.ShortAliases)
        {
            if (!IsAsciiLetter(shortAlias) || !shortCharacters.Add(shortAlias))
            {
                throw InvalidModel($"Option '{option.Name}' has an invalid or duplicate short alias.");
            }
        }

        if (!Enum.IsDefined(option.Arity))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' has an unsupported arity."));
        }

        if (!Enum.IsDefined(option.Scope))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Option '{option.Name}' has an unsupported scope."));
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

        HashSet<string> names = [with(StringComparer.Ordinal)];
        if (option.LongName is string canonicalLongName)
        {
            _ = names.Add(canonicalLongName);
        }
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
        ValidateConverter(positional.Converter, positional.ElementType ?? positional.ValueType);
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

    private static void ValidateConverter(ValueConverterRegistration? converter, Type valueType)
    {
        if (converter is not null && converter.TargetType != valueType)
        {
            throw InvalidModel("A definition-local converter must target the exact scalar or collection element type.");
        }
    }

    private static bool CanRepresentNull(Type valueType) => !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;

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

    private static bool IsImmutableDefault(object value) => value.GetType().IsValueType || value is string or Uri;

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
        if (name.Length is < 1 or > 126 || !IsAsciiLetter(name[0]))
        {
            throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Canonical option name '{name}' is invalid."));
        }

        for (int index = 1; index < name.Length; index++)
        {
            char character = name[index];
            if (!IsAsciiAlphaNumeric(character) && character != '-')
            {
                throw InvalidModel(string.Create(CultureInfo.InvariantCulture, $"Canonical option name '{name}' is invalid."));
            }
        }
    }

    private static void ValidateOptionAlias(string name) => ValidateLongOptionName(name);

    private static bool IsAsciiAlphaNumeric(char character) => IsAsciiLetter(character) || character is >= '0' and <= '9';

    private static bool IsAsciiLetter(char character) => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static ArgumentException InvalidModel(string message) => new(message);

    private sealed record NodeSnapshot(
        string[] Aliases,
        CommandBuilder[] Children,
        OptionDefinition[] Options,
        PositionalDefinition[] Positionals,
        CrossValueValidator[] CrossValidators,
        CommandHandler? Handler,
        OptionPackSnapshot[] Packs,
        Dictionary<OptionDefinition, OptionPackGroupMetadata> PackGroups);

    private readonly record struct TraversalFrame(CommandBuilder Builder, int Depth, bool IsExit);
}
