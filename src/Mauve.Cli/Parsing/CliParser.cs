using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Parsing;

/// <summary>Parses an already-tokenized argument vector against a validated command model.</summary>
/// <param name="converters">Explicit type converters.</param>
/// <param name="numericCulture">The optional culture whose decimal separator is used for numeric conversion.</param>
public sealed class CliParser(ConverterRegistry? converters = null, CultureInfo? numericCulture = null)
{
    private readonly ConverterRegistry _converters = converters ?? ConverterRegistry.Empty();
    private readonly CultureInfo _numericCulture = ValueConverterEngine.CreateNumericCulture(numericCulture);

    /// <summary>Parses an argument vector without shell tokenization or execution.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Tokens excluding the executable name.</param>
    /// <returns>An immutable parse result.</returns>
    /// <exception cref="ArgumentNullException">The model, vector, or a token is null.</exception>
    /// <exception cref="ArgumentException">The vector has an invalid shape.</exception>
    public ParseResult Parse(CommandModel model, IReadOnlyList<string> arguments)
    {
        return ParseCore(model, arguments, CancellationToken.None);
    }

    internal ParseResult ParseForInvocation(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return ParseCore(model, arguments, cancellationToken);
    }

    private ParseResult ParseCore(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        string[] tokens = CopyAndValidateArguments(arguments, model.Limits, out ParseDiagnostic? inputLimit);
        if (inputLimit is not null)
        {
            return InvalidResult(tokens, inputLimit);
        }

        ParseDiagnostic? unsupportedDefinition = FindUnsupportedDefinition(model.Root);
        if (unsupportedDefinition is not null)
        {
            return InvalidResult(tokens, unsupportedDefinition);
        }

        DiagnosticBuffer diagnostics = new(model.Limits.MaximumDiagnostics);
        List<UnmatchedToken> unmatched = [];
        List<CommandDefinition> selectedPath = [model.Root];
        List<OptionOccurrence> optionOccurrences = [];
        List<PositionalOccurrence> positionalOccurrences = [];

        CommandDefinition current = model.Root;
        bool commandSelectionOpen = true;
        bool endOfOptions = false;
        for (int index = 0; index < tokens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = tokens[index];
            if (!endOfOptions && token == "--")
            {
                endOfOptions = true;
                commandSelectionOpen = false;
                continue;
            }

            if (!endOfOptions && IsOptionCandidate(token))
            {
                ScanOption(tokens, ref index, current, optionOccurrences, diagnostics, unmatched);
                continue;
            }

            CommandDefinition? child = commandSelectionOpen ? FindChild(current, token) : null;
            if (child is not null)
            {
                current = child;
                selectedPath.Add(current);
                continue;
            }

            if (commandSelectionOpen && current.Children.Count > 0 && current.Positionals.Count == 0)
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForInput(
                    ParseDiagnosticCode.UnknownCommand,
                    "The token does not name a child command.",
                    index,
                    suggestions: SuggestCommands(current, token)));
                unmatched.Add(new UnmatchedToken(index, token));
                commandSelectionOpen = false;
                continue;
            }

            positionalOccurrences.Add(new PositionalOccurrence(token, index));
            commandSelectionOpen = false;
        }

        CommandDefinition selectedCommand = selectedPath[^1];
        List<OptionOccurrence> inScopeOccurrences = [];
        foreach (OptionOccurrence occurrence in optionOccurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(occurrence.Owner, selectedCommand))
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                    ParseDiagnosticCode.OptionOutOfScope,
                    occurrence.Option,
                    occurrence.Owner,
                    occurrence.OptionIndex));
                continue;
            }

            inScopeOccurrences.Add(occurrence);
        }

        ValidateOptionOccurrences(inScopeOccurrences, selectedCommand, diagnostics);
        List<PositionalAssignment> positionalAssignments = BindPositionals(
            selectedCommand,
            positionalOccurrences,
            diagnostics,
            unmatched);

        if (diagnostics.Count > 0)
        {
            return InvalidResult(tokens, [.. selectedPath], diagnostics.ToOrderedArray(), unmatched);
        }

        ValidateHelpOptionOccurrences(inScopeOccurrences, selectedCommand, diagnostics);
        if (diagnostics.Count > 0)
        {
            return InvalidResult(tokens, [.. selectedPath], diagnostics.ToOrderedArray(), unmatched);
        }

        if (inScopeOccurrences.Exists(static occurrence =>
            occurrence.Option.IsHelpOption && occurrence.BooleanValue == true))
        {
            return new ParseResult(
                ParseOutcome.HelpRequested,
                [.. selectedPath],
                [],
                [.. unmatched],
                tokens,
                []);
        }

        ValidateRequiredInputs(selectedCommand, inScopeOccurrences, positionalAssignments, diagnostics);
        if (diagnostics.Exists(static diagnostic => diagnostic.Code != ParseDiagnosticCode.MissingRequired))
        {
            return InvalidResult(tokens, [.. selectedPath], diagnostics.ToOrderedArray(), unmatched);
        }

        List<ParsedValue> values = ConvertValues(
            selectedCommand,
            inScopeOccurrences,
            positionalAssignments,
            diagnostics,
            cancellationToken);
        if (diagnostics.Count == 0)
        {
            ApplyAbsentDefaults(selectedCommand, values, diagnostics, cancellationToken);
        }

        if (diagnostics.Count == 0)
        {
            RunValueValidators(selectedCommand, values, diagnostics, cancellationToken);
        }

        if (diagnostics.Count == 0)
        {
            RunCrossValidators(selectedCommand, values, diagnostics, cancellationToken);
        }

        return diagnostics.Count > 0
            ? InvalidResult(tokens, [.. selectedPath], diagnostics.ToOrderedArray(), unmatched)
            : new ParseResult(
            ParseOutcome.Validated,
            [.. selectedPath],
            [],
            [.. unmatched],
            tokens,
            [.. values]);
    }

    private static string[] CopyAndValidateArguments(
        IReadOnlyList<string> arguments,
        CliLimits limits,
        out ParseDiagnostic? diagnostic)
    {
        diagnostic = null;
        int count = arguments.Count;
        if (count < 0)
        {
            throw new ArgumentException("The argument vector reported a negative count.", nameof(arguments));
        }

        if (count > limits.MaximumArgumentCount)
        {
            diagnostic = ParseDiagnostic.ForInput(
                ParseDiagnosticCode.InputLimitExceeded,
                "The argument vector exceeds the maximum token count.",
                limits.MaximumArgumentCount);
            return [];
        }

        string[] copied = new string[count];
        long totalLength = 0;
        for (int index = 0; index < count; index++)
        {
            string token = arguments[index]
                ?? throw new ArgumentNullException(string.Create(CultureInfo.InvariantCulture, $"arguments[{index}]"));
            if (token.Length > limits.MaximumTokenLength)
            {
                diagnostic = ParseDiagnostic.ForInput(
                    ParseDiagnosticCode.InputLimitExceeded,
                    "An argument token exceeds the maximum length.",
                    index);
                return [];
            }

            totalLength += token.Length;
            if (totalLength > limits.MaximumTotalCodeUnits)
            {
                diagnostic = ParseDiagnostic.ForInput(
                    ParseDiagnosticCode.InputLimitExceeded,
                    "The argument vector exceeds the maximum total length.",
                    index);
                return [];
            }

            copied[index] = token;
        }

        return copied;
    }

    private ParseDiagnostic? FindUnsupportedDefinition(CommandDefinition root)
    {
        Stack<CommandDefinition> commands = new();
        commands.Push(root);
        while (commands.Count > 0)
        {
            CommandDefinition command = commands.Pop();
            foreach (OptionDefinition option in command.Options)
            {
                if (option.Arity == OptionArity.BooleanFlag)
                {
                    continue;
                }

                Type valueType = option.ElementType ?? option.ValueType;
                if (!ValueConverterEngine.IsSupported(valueType, _converters))
                {
                    return ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.InvalidModel,
                        option,
                        command,
                        null);
                }
            }

            foreach (PositionalDefinition positional in command.Positionals)
            {
                Type valueType = positional.ElementType ?? positional.ValueType;
                if (!ValueConverterEngine.IsSupported(valueType, _converters))
                {
                    return ParseDiagnostic.ForPositional(
                        ParseDiagnosticCode.InvalidModel,
                        positional,
                        command,
                        null);
                }
            }

            for (int index = command.Children.Count - 1; index >= 0; index--)
            {
                commands.Push(command.Children[index]);
            }
        }

        return null;
    }

    private static void ScanOption(
        string[] tokens,
        ref int index,
        CommandDefinition owner,
        List<OptionOccurrence> occurrences,
        DiagnosticBuffer diagnostics,
        List<UnmatchedToken> unmatched)
    {
        string token = tokens[index];
        int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        bool hasInlineValue = equalsIndex >= 0;
        string name = hasInlineValue ? token[..equalsIndex] : token;
        string? inlineValue = hasInlineValue ? token[(equalsIndex + 1)..] : null;
        int optionIndex = index;
        if (!owner.TryGetOption(name, out OptionDefinition? option))
        {
            AddDiagnostic(diagnostics, ParseDiagnostic.ForInput(
                ParseDiagnosticCode.UnknownOption,
                "The token does not name a visible option.",
                optionIndex,
                suggestions: SuggestOptions(owner, name)));
            unmatched.Add(new UnmatchedToken(optionIndex, token));
            return;
        }

        OptionAliasKind aliasKind = FindAliasKind(option, name);
        OptionOccurrence occurrence = new(owner, option, optionIndex, aliasKind);
        if (aliasKind == OptionAliasKind.Negating && hasInlineValue)
        {
            AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                ParseDiagnosticCode.UnexpectedValue,
                option,
                owner,
                optionIndex));
            occurrences.Add(occurrence);
            return;
        }

        if (option.Arity == OptionArity.BooleanFlag)
        {
            ScanBoolean(tokens, ref index, occurrence, hasInlineValue, inlineValue, equalsIndex);
            occurrences.Add(occurrence);
            return;
        }

        if (option.Arity == OptionArity.OptionalScalar && !hasInlineValue)
        {
            occurrence.Presence = ValuePresence.Bare;
            occurrences.Add(occurrence);
            return;
        }

        if (hasInlineValue)
        {
            occurrence.Presence = ValuePresence.WithValue;
            occurrence.Values.Add(new RawValue(inlineValue!, optionIndex, equalsIndex + 1));
            occurrences.Add(occurrence);
            return;
        }

        if (option.Arity == OptionArity.GreedyCollection)
        {
            while (index + 1 < tokens.Length && CanConsumeValue(tokens[index + 1], option.ElementType!))
            {
                index++;
                occurrence.Values.Add(new RawValue(tokens[index], index, null));
            }
        }
        else if (index + 1 < tokens.Length && CanConsumeValue(tokens[index + 1], option.ElementType ?? option.ValueType))
        {
            index++;
            occurrence.Values.Add(new RawValue(tokens[index], index, null));
        }

        if (occurrence.Values.Count == 0)
        {
            AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                ParseDiagnosticCode.MissingValue,
                option,
                owner,
                optionIndex));
        }
        else
        {
            occurrence.Presence = ValuePresence.WithValue;
        }

        occurrences.Add(occurrence);
    }

    private static void ScanBoolean(
        string[] tokens,
        ref int index,
        OptionOccurrence occurrence,
        bool hasInlineValue,
        string? inlineValue,
        int equalsIndex)
    {
        if (occurrence.AliasKind == OptionAliasKind.Negating)
        {
            occurrence.Presence = ValuePresence.Bare;
            occurrence.BooleanValue = false;
            return;
        }

        if (hasInlineValue)
        {
            occurrence.Presence = ValuePresence.WithValue;
            occurrence.Values.Add(new RawValue(inlineValue!, index, equalsIndex + 1));
            if (TryBooleanLiteral(inlineValue!, out bool inlineBoolean))
            {
                occurrence.BooleanValue = inlineBoolean;
            }

            return;
        }

        if (index + 1 < tokens.Length && TryBooleanLiteral(tokens[index + 1], out bool explicitValue))
        {
            index++;
            occurrence.Presence = ValuePresence.WithValue;
            occurrence.Values.Add(new RawValue(tokens[index], index, null));
            occurrence.BooleanValue = explicitValue;
            return;
        }

        occurrence.Presence = ValuePresence.Bare;
        occurrence.BooleanValue = true;
    }

    private static void ValidateOptionOccurrences(
        List<OptionOccurrence> occurrences,
        CommandDefinition selectedCommand,
        DiagnosticBuffer diagnostics)
    {
        Dictionary<(CommandDefinition Owner, OptionDefinition Option), List<OptionOccurrence>> groups = [];
        foreach (OptionOccurrence occurrence in occurrences)
        {
            (CommandDefinition Owner, OptionDefinition Option) key = (occurrence.Owner, occurrence.Option);
            if (!groups.TryGetValue(key, out List<OptionOccurrence>? group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(occurrence);
        }

        foreach (OptionDefinition option in selectedCommand.Options)
        {
            _ = groups.TryGetValue((selectedCommand, option), out List<OptionOccurrence>? occurrencesForOption);
            if (occurrencesForOption is not null
                && option.Arity is not (OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection))
            {
                for (int index = 1; index < occurrencesForOption.Count; index++)
                {
                    OptionOccurrence repeated = occurrencesForOption[index];
                    AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.DuplicateOption,
                        option,
                        selectedCommand,
                        repeated.OptionIndex));
                }
            }

        }
    }

    private static void ValidateHelpOptionOccurrences(
        List<OptionOccurrence> occurrences,
        CommandDefinition selectedCommand,
        DiagnosticBuffer diagnostics)
    {
        foreach (OptionOccurrence occurrence in occurrences)
        {
            if (occurrence.Option.IsHelpOption
                && occurrence.Values.Count > 0
                && occurrence.BooleanValue is null)
            {
                RawValue raw = occurrence.Values[0];
                AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                    ParseDiagnosticCode.InvalidValue,
                    occurrence.Option,
                    selectedCommand,
                    occurrence.OptionIndex,
                    raw.TokenIndex,
                    raw.ValueOffset,
                    "boolean.invalid"));
            }
        }
    }

    private static void ValidateRequiredInputs(
        CommandDefinition selectedCommand,
        List<OptionOccurrence> optionOccurrences,
        List<PositionalAssignment> positionalAssignments,
        DiagnosticBuffer diagnostics)
    {
        foreach (OptionDefinition option in selectedCommand.Options)
        {
            if (option.IsRequired
                && !optionOccurrences.Exists(occurrence => ReferenceEquals(occurrence.Option, option)))
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                    ParseDiagnosticCode.MissingRequired,
                    option,
                    selectedCommand,
                    null));
            }
        }

        foreach (PositionalAssignment assignment in positionalAssignments)
        {
            if (assignment.Occurrences.Count < assignment.Definition.MinimumValues)
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForPositional(
                    ParseDiagnosticCode.MissingRequired,
                    assignment.Definition,
                    selectedCommand,
                    null));
            }
        }
    }

    private static List<PositionalAssignment> BindPositionals(
        CommandDefinition selectedCommand,
        List<PositionalOccurrence> occurrences,
        DiagnosticBuffer diagnostics,
        List<UnmatchedToken> unmatched)
    {
        List<PositionalAssignment> assignments = [];
        int nextOccurrence = 0;
        foreach (PositionalDefinition positional in selectedCommand.Positionals)
        {
            int available = occurrences.Count - nextOccurrence;
            int count = positional.MaximumValues is null
                ? available
                : Math.Min(available, positional.MaximumValues.Value);
            List<PositionalOccurrence> assigned = [];
            for (int index = 0; index < count; index++)
            {
                assigned.Add(occurrences[nextOccurrence++]);
            }

            assignments.Add(new PositionalAssignment(positional, assigned));
        }

        while (nextOccurrence < occurrences.Count)
        {
            PositionalOccurrence occurrence = occurrences[nextOccurrence++];
            AddDiagnostic(diagnostics, ParseDiagnostic.ForInput(
                ParseDiagnosticCode.UnexpectedArgument,
                "The token cannot be assigned to a positional argument.",
                occurrence.Index));
            unmatched.Add(new UnmatchedToken(occurrence.Index, occurrence.Value));
        }

        return assignments;
    }

    private List<ParsedValue> ConvertValues(
        CommandDefinition selectedCommand,
        List<OptionOccurrence> occurrences,
        List<PositionalAssignment> positionals,
        DiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        Dictionary<int, ConversionAttempt> convertedByTokenIndex = ConvertTokensInOrder(
            selectedCommand,
            occurrences,
            positionals,
            diagnostics,
            cancellationToken);
        ValidateBooleanOccurrences(selectedCommand, occurrences, diagnostics);
        if (diagnostics.Count > 0)
        {
            return [];
        }

        List<ParsedValue> values = [];
        foreach (OptionDefinition option in selectedCommand.Options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<OptionOccurrence> optionOccurrences = [.. occurrences
                .Where(occurrence => ReferenceEquals(occurrence.Owner, selectedCommand)
                    && ReferenceEquals(occurrence.Option, option))];
            if (optionOccurrences.Count == 0)
            {
                if (option.Arity == OptionArity.BooleanFlag)
                {
                    values.Add(new ParsedValue(
                        selectedCommand,
                        option,
                        null,
                        false,
                        true,
                        ValuePresence.Absent,
                        ValueOrigin.Default,
                        []));
                }
                else
                {
                    values.Add(new ParsedValue(
                        selectedCommand,
                        option,
                        null,
                        null,
                        false,
                        ValuePresence.Absent,
                        ValueOrigin.Absent,
                        []));
                }

                continue;
            }

            if (option.Arity == OptionArity.BooleanFlag)
            {
                OptionOccurrence flag = optionOccurrences[0];
                values.Add(new ParsedValue(
                    selectedCommand,
                    option,
                    null,
                    flag.BooleanValue ?? true,
                    true,
                    flag.Presence,
                    ValueOrigin.Explicit,
                    flag.GetTokenIndices()));
                continue;
            }

            if (option.Arity == OptionArity.OptionalScalar
                && optionOccurrences[0].Presence == ValuePresence.Bare)
            {
                OptionOccurrence bareOccurrence = optionOccurrences[0];
                object? bareValue = option.BareValuePolicy switch
                {
                    BareValuePolicy.Null => null,
                    BareValuePolicy.Constant => option.BareValue,
                    BareValuePolicy.Factory => InvokeFactory(option.BareValueFactory!, cancellationToken),
                    BareValuePolicy.Unspecified => throw new NotImplementedException(),
                    _ => throw new InvalidOperationException("The model contains an invalid optional-value policy.")
                };
                if (!IsValueCompatible(option.ValueType, bareValue, option.AllowsNull))
                {
                    AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.ValidationFailed,
                        option,
                        selectedCommand,
                        bareOccurrence.OptionIndex,
                        failureCode: "default.invalid"));
                    continue;
                }

                values.Add(new ParsedValue(
                    selectedCommand,
                    option,
                    null,
                    bareValue,
                    true,
                    ValuePresence.Bare,
                    ValueOrigin.Explicit,
                    bareOccurrence.GetTokenIndices()));
                continue;
            }

            List<object?> convertedValues = [];
            List<int> tokenIndices = [];
            bool conversionSucceeded = true;
            foreach (OptionOccurrence occurrence in optionOccurrences)
            {
                foreach (RawValue raw in occurrence.Values)
                {
                    ConversionAttempt attempt = convertedByTokenIndex[raw.TokenIndex];
                    if (!attempt.Succeeded)
                    {
                        conversionSucceeded = false;
                    }
                    else
                    {
                        convertedValues.Add(attempt.Value);
                    }

                    tokenIndices.Add(occurrence.OptionIndex);
                    if (raw.TokenIndex != occurrence.OptionIndex)
                    {
                        tokenIndices.Add(raw.TokenIndex);
                    }
                }
            }

            if (!conversionSucceeded)
            {
                continue;
            }

            object? value;
            if (IsCollection(option.Arity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                value = CreateCollection(option.ElementType!, convertedValues);
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                value = convertedValues[0];
            }
            values.Add(new ParsedValue(
                selectedCommand,
                option,
                null,
                value,
                true,
                optionOccurrences.Any(static occurrence => occurrence.Presence == ValuePresence.WithValue)
                    ? ValuePresence.WithValue
                    : ValuePresence.Bare,
                ValueOrigin.Explicit,
                [.. tokenIndices.Distinct().Order()]));
        }

        foreach (PositionalAssignment assignment in positionals)
        {
            List<object?> convertedValues = [];
            List<int> tokenIndices = [];
            bool conversionSucceeded = true;
            foreach (PositionalOccurrence occurrence in assignment.Occurrences)
            {
                ConversionAttempt attempt = convertedByTokenIndex[occurrence.Index];
                if (!attempt.Succeeded)
                {
                    conversionSucceeded = false;
                }
                else
                {
                    convertedValues.Add(attempt.Value);
                }

                tokenIndices.Add(occurrence.Index);
            }

            if (!conversionSucceeded)
            {
                continue;
            }

            bool isRest = assignment.Definition.MaximumValues is null;
            object? value = isRest
                ? CreateCollection(assignment.Definition.ElementType!, convertedValues)
                : convertedValues.Count == 0 ? null : convertedValues[0];
            bool available = isRest || convertedValues.Count > 0;
            bool supplied = convertedValues.Count > 0;
            values.Add(new ParsedValue(
                selectedCommand,
                null,
                assignment.Definition,
                value,
                available,
                supplied ? ValuePresence.WithValue : ValuePresence.Absent,
                supplied ? ValueOrigin.Explicit : isRest ? ValueOrigin.Default : ValueOrigin.Absent,
                [.. tokenIndices]));
        }

        return values;
    }

    private static void ValidateBooleanOccurrences(
        CommandDefinition selectedCommand,
        List<OptionOccurrence> occurrences,
        DiagnosticBuffer diagnostics)
    {
        foreach (OptionOccurrence occurrence in occurrences)
        {
            if (occurrence.Option.Arity == OptionArity.BooleanFlag
                && occurrence.Values.Count > 0
                && occurrence.BooleanValue is null)
            {
                RawValue raw = occurrence.Values[0];
                AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                    ParseDiagnosticCode.InvalidValue,
                    occurrence.Option,
                    selectedCommand,
                    occurrence.OptionIndex,
                    raw.TokenIndex,
                    raw.ValueOffset,
                    "boolean.invalid"));
            }
        }
    }

    private void ApplyAbsentDefaults(
        CommandDefinition selectedCommand,
        List<ParsedValue> values,
        DiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < values.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedValue entry = values[index];
            if (entry.IsAvailable
                || entry.Origin != ValueOrigin.Absent
                || entry.Option?.IsRequired == true
                || entry.Positional?.MinimumValues > 0)
            {
                continue;
            }

            DefaultValuePolicy policy = entry.Option?.DefaultValuePolicy
                ?? entry.Positional!.DefaultValuePolicy;
            if (policy == DefaultValuePolicy.None)
            {
                continue;
            }

            object? defaultValue = policy switch
            {
                DefaultValuePolicy.Constant => entry.Option?.DefaultValue ?? entry.Positional?.DefaultValue,
                DefaultValuePolicy.Factory => entry.Option?.DefaultValueFactory is Func<object?> optionFactory
                    ? InvokeFactory(optionFactory, cancellationToken)
                    : InvokeFactory(entry.Positional!.DefaultValueFactory!, cancellationToken),
                DefaultValuePolicy.None => throw new InvalidOperationException("The model contains an invalid default-value policy."),
                _ => throw new InvalidOperationException("The model contains an invalid default-value policy.")
            };
            Type targetType = entry.Option?.ValueType ?? entry.Positional!.ValueType;
            bool allowsNull = entry.Option?.AllowsNull ?? entry.Positional!.AllowsNull;
            Type? elementType = entry.Option?.ElementType ?? entry.Positional?.ElementType;
            if (elementType is not null)
            {
                if (defaultValue is not IEnumerable sequence)
                {
                    AddInvalidDefaultDiagnostic(selectedCommand, entry, diagnostics);
                    continue;
                }

                List<object?> elements = [];
                bool compatibleElements = true;
                foreach (object? element in sequence)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsValueCompatible(elementType, element, allowsNull))
                    {
                        compatibleElements = false;
                        break;
                    }

                    elements.Add(element);
                }

                if (!compatibleElements)
                {
                    AddInvalidDefaultDiagnostic(selectedCommand, entry, diagnostics);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                defaultValue = ValueConverterEngine.CreateReadOnlyCollection(elementType, elements, _converters);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!IsValueCompatible(targetType, defaultValue, allowsNull))
            {
                AddInvalidDefaultDiagnostic(selectedCommand, entry, diagnostics);
                continue;
            }

            values[index] = new ParsedValue(
                selectedCommand,
                entry.Option,
                entry.Positional,
                defaultValue,
                true,
                ValuePresence.Absent,
                ValueOrigin.Default,
                []);
        }
    }

    private static void AddInvalidDefaultDiagnostic(
        CommandDefinition selectedCommand,
        ParsedValue entry,
        DiagnosticBuffer diagnostics)
    {
        AddDiagnostic(diagnostics, entry.Option is not null
            ? ParseDiagnostic.ForOption(
                ParseDiagnosticCode.ValidationFailed,
                entry.Option,
                selectedCommand,
                null,
                failureCode: "default.invalid")
            : ParseDiagnostic.ForPositional(
                ParseDiagnosticCode.ValidationFailed,
                entry.Positional!,
                selectedCommand,
                null,
                failureCode: "default.invalid"));
    }

    private static void RunValueValidators(
        CommandDefinition selectedCommand,
        List<ParsedValue> values,
        DiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        ValidationContext context = new(selectedCommand, [.. values]);
        foreach (ParsedValue entry in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsAvailable)
            {
                continue;
            }

            IReadOnlyList<IValueValidator> validators = entry.Option?.Validators
                ?? entry.Positional!.Validators;
            foreach (IValueValidator validator in validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValueValidationOutcome outcome = validator.Validate(entry.Value, context);
                cancellationToken.ThrowIfCancellationRequested();
                string? failureCode = GetValidationFailureCode(outcome);
                if (failureCode is null)
                {
                    continue;
                }

                int? tokenIndex = entry.TokenIndices.Count == 0 ? null : entry.TokenIndices[0];
                AddDiagnostic(diagnostics, entry.Option is not null
                    ? ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.ValidationFailed,
                        entry.Option,
                        selectedCommand,
                        tokenIndex,
                        tokenIndex,
                        failureCode: failureCode)
                    : ParseDiagnostic.ForPositional(
                        ParseDiagnosticCode.ValidationFailed,
                        entry.Positional!,
                        selectedCommand,
                        tokenIndex,
                        tokenIndex,
                        failureCode: failureCode));
            }
        }
    }

    private static void RunCrossValidators(
        CommandDefinition selectedCommand,
        List<ParsedValue> values,
        DiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        ValidationContext context = new(selectedCommand, [.. values]);
        foreach (CrossValueValidator validator in selectedCommand.CrossValidators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValueValidationOutcome outcome = validator(context);
            cancellationToken.ThrowIfCancellationRequested();
            string? failureCode = GetValidationFailureCode(outcome);
            if (failureCode is not null)
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForCrossValidation(selectedCommand, failureCode));
            }
        }
    }

    private static string? GetValidationFailureCode(ValueValidationOutcome outcome)
    {
        return outcome.Succeeded
            ? outcome.FailureCode is null
                ? null
                : throw new InvalidOperationException("A successful validator returned a failure code.")
            : ValidationFailureCode.IsValid(outcome.FailureCode ?? string.Empty)
                ? outcome.FailureCode
                : throw new InvalidOperationException("A validator returned an invalid failure code.");
    }

    private Dictionary<int, ConversionAttempt> ConvertTokensInOrder(
        CommandDefinition selectedCommand,
        List<OptionOccurrence> occurrences,
        List<PositionalAssignment> positionals,
        DiagnosticBuffer diagnostics,
        CancellationToken cancellationToken)
    {
        List<ConversionRequest> requests = [];
        foreach (OptionOccurrence occurrence in occurrences)
        {
            if (occurrence.Option.Arity == OptionArity.BooleanFlag
                || (occurrence.Option.Arity == OptionArity.OptionalScalar && occurrence.Presence == ValuePresence.Bare))
            {
                continue;
            }

            Type targetType = occurrence.Option.ElementType ?? occurrence.Option.ValueType;
            foreach (RawValue raw in occurrence.Values)
            {
                requests.Add(new ConversionRequest(
                    raw.Value,
                    targetType,
                    occurrence.Option.Name,
                    occurrence.Option.IsSensitive,
                    raw.TokenIndex,
                    occurrence.OptionIndex,
                    raw.ValueOffset,
                    occurrence.Option,
                    null));
            }
        }

        foreach (PositionalAssignment assignment in positionals)
        {
            Type targetType = assignment.Definition.ElementType ?? assignment.Definition.ValueType;
            foreach (PositionalOccurrence occurrence in assignment.Occurrences)
            {
                requests.Add(new ConversionRequest(
                    occurrence.Value,
                    targetType,
                    assignment.Definition.Name,
                    assignment.Definition.IsSensitive,
                    occurrence.Index,
                    occurrence.Index,
                    null,
                    null,
                    assignment.Definition));
            }
        }

        Dictionary<int, ConversionAttempt> convertedByTokenIndex = [];
        foreach (ConversionRequest request in requests.OrderBy(static request => request.TokenIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversionContext context = new(
                request.TargetType,
                request.DefinitionName,
                request.TokenIndex,
                _numericCulture,
                request.IsSensitive);
            bool conversionSucceeded = ValueConverterEngine.TryConvert(
                request.RawValue,
                request.TargetType,
                context,
                _converters,
                out object? converted,
                out string failureCode);
            cancellationToken.ThrowIfCancellationRequested();
            bool succeeded = conversionSucceeded
                && IsValueCompatible(
                    request.TargetType,
                    converted,
                    request.Option?.AllowsNull ?? request.Positional!.AllowsNull);
            if (conversionSucceeded && !succeeded)
            {
                failureCode = "conversion.invalid";
            }

            if (!succeeded)
            {
                ParseDiagnostic diagnostic = request.Option is not null
                    ? ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.InvalidValue,
                        request.Option,
                        selectedCommand,
                        request.DefinitionTokenIndex,
                        request.TokenIndex,
                        request.ValueOffset,
                        failureCode)
                    : ParseDiagnostic.ForPositional(
                        ParseDiagnosticCode.InvalidValue,
                        request.Positional!,
                        selectedCommand,
                        request.DefinitionTokenIndex,
                        request.TokenIndex,
                        request.ValueOffset,
                        failureCode);
                AddDiagnostic(diagnostics, diagnostic);
            }

            convertedByTokenIndex.Add(request.TokenIndex, new ConversionAttempt(succeeded, converted));
        }

        return convertedByTokenIndex;
    }

    private object CreateCollection(Type elementType, List<object?> values)
    {
        return ValueConverterEngine.CreateReadOnlyCollection(elementType, values, _converters);
    }

    private static object? InvokeFactory(Func<object?> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object? value = factory();
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    private static bool IsValueCompatible(Type type, object? value, bool allowsNull)
    {
        return (value is null && allowsNull)
            || (value is not null
                && (type.IsInstanceOfType(value)
                    || Nullable.GetUnderlyingType(type)?.IsInstanceOfType(value) == true));
    }

    private static bool IsCollection(OptionArity arity)
    {
        return arity is OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection;
    }

    private static bool CanConsumeValue(string token, Type valueType)
    {
        return token != "--"
            && (!IsOptionCandidate(token)
                || (ValueConverterEngine.IsNumericType(valueType) && IsNegativeNumericCandidate(token)));
    }

    private static bool IsOptionCandidate(string token)
    {
        return token.Length > 1 && token[0] == '-';
    }

    private static bool IsNegativeNumericCandidate(string token)
    {
        return token.Length > 1
            && token[0] == '-'
            && (char.IsAsciiDigit(token[1])
                || (token.Length > 2 && token[1] == '.' && char.IsAsciiDigit(token[2])));
    }

    private static bool TryBooleanLiteral(string token, out bool value)
    {
        if (token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static OptionAliasKind FindAliasKind(OptionDefinition option, string name)
    {
        foreach (OptionAlias alias in option.Aliases)
        {
            if (alias.Name == name)
            {
                return alias.Kind;
            }
        }

        return OptionAliasKind.Normal;
    }

    private static CommandDefinition? FindChild(CommandDefinition command, string name)
    {
        foreach (CommandDefinition candidate in command.Children)
        {
            if (candidate.Name == name || candidate.Aliases.Contains(name, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[] SuggestCommands(CommandDefinition command, string input)
    {
        if (input.Length > 128)
        {
            return [];
        }

        List<NameCandidate> candidates = [];
        foreach (CommandDefinition child in command.Children)
        {
            if (child.IsHidden)
            {
                continue;
            }

            if (!TryAddCandidate(candidates, child.Name, child.Name))
            {
                break;
            }

            foreach (string alias in child.Aliases)
            {
                if (!TryAddCandidate(candidates, alias, child.Name))
                {
                    return FindSuggestions(candidates, input);
                }
            }
        }

        return FindSuggestions(candidates, input);
    }

    private static string[] SuggestOptions(CommandDefinition command, string input)
    {
        if (input.Length > 128)
        {
            return [];
        }

        List<NameCandidate> candidates = [];
        foreach (OptionDefinition option in command.Options)
        {
            if (option.IsHidden)
            {
                continue;
            }

            if (!TryAddCandidate(candidates, option.Name, option.Name))
            {
                break;
            }

            foreach (OptionAlias alias in option.Aliases)
            {
                if (!TryAddCandidate(candidates, alias.Name, option.Name))
                {
                    return FindSuggestions(candidates, input);
                }
            }
        }

        return FindSuggestions(candidates, input);
    }

    private static bool TryAddCandidate(List<NameCandidate> candidates, string spelling, string canonical)
    {
        if (candidates.Count == 256)
        {
            return false;
        }

        candidates.Add(new NameCandidate(spelling, canonical));
        return true;
    }

    private static string[] FindSuggestions(List<NameCandidate> candidates, string input)
    {
        if (input.Length > 128)
        {
            return [];
        }

        Dictionary<string, SuggestionMatch> matches = [with(StringComparer.Ordinal)];
        foreach (NameCandidate candidate in candidates)
        {
            if (candidate.Spelling.Length > 128)
            {
                continue;
            }

            bool isPrefix = candidate.Spelling.StartsWith(input, StringComparison.Ordinal);
            int distance = GetBoundedEditDistance(candidate.Spelling, input);
            if ((!isPrefix) && (distance > 2))
            {
                continue;
            }

            SuggestionMatch match = new(candidate.Canonical, isPrefix, distance);
            if (!matches.TryGetValue(candidate.Canonical, out SuggestionMatch existing)
                || IsBetterSuggestion(match, existing))
            {
                matches[candidate.Canonical] = match;
            }
        }

        return [.. matches.Values
            .OrderByDescending(static match => match.IsPrefix)
            .ThenBy(static match => match.Distance)
            .ThenBy(static match => match.Canonical, StringComparer.Ordinal)
            .Take(5)
            .Select(static match => match.Canonical)];
    }

    private static bool IsBetterSuggestion(SuggestionMatch candidate, SuggestionMatch existing)
    {
        return (candidate.IsPrefix && !existing.IsPrefix)
            || (candidate.IsPrefix == existing.IsPrefix && candidate.Distance < existing.Distance);
    }

    private static int GetBoundedEditDistance(string left, string right)
    {
        const int maximumDistance = 2;
        if (Math.Abs(left.Length - right.Length) > maximumDistance)
        {
            return maximumDistance + 1;
        }

        int[] previous = new int[right.Length + 1];
        int[] current = new int[right.Length + 1];
        Array.Fill(previous, maximumDistance + 1);
        for (int column = 0; column <= Math.Min(right.Length, maximumDistance); column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= left.Length; row++)
        {
            Array.Fill(current, maximumDistance + 1);
            current[0] = row <= maximumDistance ? row : maximumDistance + 1;
            int firstColumn = Math.Max(1, row - maximumDistance);
            int lastColumn = Math.Min(right.Length, row + maximumDistance);
            int rowMinimum = current[0];
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > maximumDistance)
            {
                return maximumDistance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static void AddDiagnostic(DiagnosticBuffer diagnostics, ParseDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }

    private static ParseResult InvalidResult(string[] arguments, ParseDiagnostic diagnostic)
    {
        return InvalidResult(arguments, [], [diagnostic], []);
    }

    private static ParseResult InvalidResult(
        string[] arguments,
        CommandDefinition[] selectedPath,
        ParseDiagnostic[] diagnostics,
        List<UnmatchedToken> unmatched)
    {
        return new ParseResult(
            ParseOutcome.Invalid,
            selectedPath,
            diagnostics,
            [.. unmatched],
            arguments,
            []);
    }

    private sealed class DiagnosticBuffer(int maximum)
    {
        private readonly List<ParseDiagnostic> _items = [];

        public int Count => _items.Count;

        public bool Exists(Predicate<ParseDiagnostic> predicate)
        {
            return _items.Exists(predicate);
        }

        public void Add(ParseDiagnostic diagnostic)
        {
            if (maximum == 1)
            {
                if (_items.Count == 0)
                {
                    _items.Add(diagnostic);
                }
                else if (_items[0].Code != ParseDiagnosticCode.DiagnosticLimitExceeded)
                {
                    _items[0] = CreateLimitDiagnostic();
                }

                return;
            }

            if (_items.Count < maximum - 1)
            {
                _items.Add(diagnostic);
            }
            else if (_items.Count == maximum - 1)
            {
                _items.Add(CreateLimitDiagnostic());
            }
        }

        public ParseDiagnostic[] ToOrderedArray()
        {
            return [.. _items
                .Select(static (diagnostic, index) => new DiagnosticOrder(diagnostic, index))
                .OrderBy(static item => item.Diagnostic.OrderingPhase)
                .ThenBy(static item => item.Diagnostic.OrderingPhase is 0 or 1
                    ? item.Diagnostic.ValueTokenIndex ?? item.Diagnostic.TokenIndex ?? int.MaxValue
                    : 0)
                .ThenBy(static item => item.Diagnostic.OrderingPhase is 0 or 1
                    ? item.Diagnostic.Code.ToString()
                    : string.Empty, StringComparer.Ordinal)
                .ThenBy(static item => item.Index)
                .Select(static item => item.Diagnostic)];
        }

        private static ParseDiagnostic CreateLimitDiagnostic()
        {
            return ParseDiagnostic.ForInput(
                ParseDiagnosticCode.DiagnosticLimitExceeded,
                "The parse result exceeded the maximum diagnostic count.");
        }
    }

    private sealed record NameCandidate(string Spelling, string Canonical);

    private readonly record struct SuggestionMatch(string Canonical, bool IsPrefix, int Distance);

    private readonly record struct DiagnosticOrder(ParseDiagnostic Diagnostic, int Index);

    private sealed class OptionOccurrence(
        CommandDefinition owner,
        OptionDefinition option,
        int optionIndex,
        OptionAliasKind aliasKind)
    {
        public CommandDefinition Owner { get; } = owner;

        public OptionDefinition Option { get; } = option;

        public int OptionIndex { get; } = optionIndex;

        public OptionAliasKind AliasKind { get; } = aliasKind;

        public List<RawValue> Values { get; } = [];

        public ValuePresence Presence { get; set; }

        public bool? BooleanValue { get; set; }

        public int[] GetTokenIndices()
        {
            return Values.Count == 0
                ? [OptionIndex]
                : [OptionIndex, .. Values.Select(static value => value.TokenIndex).Distinct().Where(index => index != OptionIndex).Order()];
        }
    }

    private sealed record RawValue(string Value, int TokenIndex, int? ValueOffset);

    private sealed record ConversionRequest(
        string RawValue,
        Type TargetType,
        string DefinitionName,
        bool IsSensitive,
        int TokenIndex,
        int DefinitionTokenIndex,
        int? ValueOffset,
        OptionDefinition? Option,
        PositionalDefinition? Positional);

    private sealed record ConversionAttempt(bool Succeeded, object? Value);

    private sealed record PositionalOccurrence(string Value, int Index);

    private sealed record PositionalAssignment(PositionalDefinition Definition, List<PositionalOccurrence> Occurrences);
}
