using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Parsing;

/// <summary>Parses an already-tokenized argument vector against a validated command model.</summary>
/// <param name="converters">Explicit type converters.</param>
/// <param name="numericCulture">The optional culture whose decimal separator is used for numeric conversion.</param>
public sealed class CliParser(ConverterRegistry? converters = null, CultureInfo? numericCulture = null)
{
    private const int _maximumArguments = 4096;
    private const int _maximumTokenLength = 16384;
    private const long _maximumTotalLength = 1048576;
    private const int _maximumDiagnostics = 64;

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
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);

        string[] tokens = CopyAndValidateArguments(arguments, out ParseDiagnostic? inputLimit);
        if (inputLimit is not null)
        {
            return InvalidResult(tokens, inputLimit);
        }

        ParseDiagnostic? unsupportedDefinition = FindUnsupportedDefinition(model.Root);
        if (unsupportedDefinition is not null)
        {
            return InvalidResult(tokens, unsupportedDefinition);
        }

        List<ParseDiagnostic> diagnostics = [];
        List<UnmatchedToken> unmatched = [];
        List<CommandDefinition> selectedPath = [model.Root];
        List<OptionOccurrence> optionOccurrences = [];
        List<PositionalOccurrence> positionalOccurrences = [];

        CommandDefinition current = model.Root;
        bool commandSelectionOpen = true;
        bool endOfOptions = false;
        for (int index = 0; index < tokens.Length; index++)
        {
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
                    index));
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

        if (diagnostics.Exists(static diagnostic => diagnostic.Code != ParseDiagnosticCode.MissingRequired))
        {
            return InvalidResult(tokens, [.. selectedPath], diagnostics, unmatched);
        }

        List<ParsedValue> values = ConvertValues(
            selectedCommand,
            inScopeOccurrences,
            positionalAssignments,
            diagnostics);
        return diagnostics.Count > 0
            ? InvalidResult(tokens, [.. selectedPath], diagnostics, unmatched)
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
        out ParseDiagnostic? diagnostic)
    {
        diagnostic = null;
        int count = arguments.Count;
        if (count < 0)
        {
            throw new ArgumentException("The argument vector reported a negative count.", nameof(arguments));
        }

        if (count > _maximumArguments)
        {
            diagnostic = ParseDiagnostic.ForInput(
                ParseDiagnosticCode.InputLimitExceeded,
                "The argument vector exceeds the maximum token count.",
                _maximumArguments);
            return [];
        }

        string[] copied = new string[count];
        long totalLength = 0;
        for (int index = 0; index < count; index++)
        {
            string token = arguments[index]
                ?? throw new ArgumentNullException(string.Create(CultureInfo.InvariantCulture, $"arguments[{index}]"));
            if (token.Length > _maximumTokenLength)
            {
                diagnostic = ParseDiagnostic.ForInput(
                    ParseDiagnosticCode.InputLimitExceeded,
                    "An argument token exceeds the maximum length.",
                    index);
                return [];
            }

            totalLength += token.Length;
            if (totalLength > _maximumTotalLength)
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
        List<ParseDiagnostic> diagnostics,
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
                optionIndex));
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
        List<ParseDiagnostic> diagnostics)
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

            bool supplied = occurrencesForOption is { Count: > 0 };
            if (option.IsRequired && !supplied)
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                    ParseDiagnosticCode.MissingRequired,
                    option,
                    selectedCommand,
                    null));
            }
        }
    }

    private static List<PositionalAssignment> BindPositionals(
        CommandDefinition selectedCommand,
        List<PositionalOccurrence> occurrences,
        List<ParseDiagnostic> diagnostics,
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

            if (assigned.Count < positional.MinimumValues)
            {
                AddDiagnostic(diagnostics, ParseDiagnostic.ForPositional(
                    ParseDiagnosticCode.MissingRequired,
                    positional,
                    selectedCommand,
                    null));
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
        List<ParseDiagnostic> diagnostics)
    {
        Dictionary<int, ConversionAttempt> convertedByTokenIndex = ConvertTokensInOrder(
            selectedCommand,
            occurrences,
            positionals,
            diagnostics);
        List<ParsedValue> values = [];
        foreach (OptionDefinition option in selectedCommand.Options)
        {
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
                object flagValue;
                if (flag.Values.Count == 0)
                {
                    flagValue = flag.BooleanValue ?? true;
                }
                else if (flag.BooleanValue is bool explicitBoolean)
                {
                    flagValue = explicitBoolean;
                }
                else
                {
                    RawValue raw = flag.Values[0];
                    AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.InvalidValue,
                        option,
                        selectedCommand,
                        flag.OptionIndex,
                        raw.TokenIndex,
                        raw.ValueOffset,
                        "boolean.invalid"));
                    continue;
                }

                values.Add(new ParsedValue(
                    selectedCommand,
                    option,
                    null,
                    flagValue,
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
                    BareValuePolicy.Factory => option.BareValueFactory!(),
                    BareValuePolicy.Unspecified => throw new NotImplementedException(),
                    _ => throw new InvalidOperationException("The model contains an invalid optional-value policy.")
                };
                if (!IsValueCompatible(option.ValueType, bareValue, option.AllowsNull))
                {
                    AddDiagnostic(diagnostics, ParseDiagnostic.ForOption(
                        ParseDiagnosticCode.InvalidValue,
                        option,
                        selectedCommand,
                        bareOccurrence.OptionIndex,
                        failureCode: "conversion.invalid"));
                    continue;
                }

                values.Add(new ParsedValue(
                    selectedCommand,
                    option,
                    null,
                    bareValue,
                    true,
                    ValuePresence.Bare,
                    ValueOrigin.Default,
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

            object? value = IsCollection(option.Arity)
                ? CreateCollection(option.ElementType!, convertedValues)
                : convertedValues[0];
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

    private Dictionary<int, ConversionAttempt> ConvertTokensInOrder(
        CommandDefinition selectedCommand,
        List<OptionOccurrence> occurrences,
        List<PositionalAssignment> positionals,
        List<ParseDiagnostic> diagnostics)
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

    private static Array CreateCollection(Type elementType, List<object?> values)
    {
        Array collection = Array.CreateInstance(elementType, values.Count);
        for (int index = 0; index < values.Count; index++)
        {
            collection.SetValue(values[index], index);
        }

        return collection;
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

    private static void AddDiagnostic(List<ParseDiagnostic> diagnostics, ParseDiagnostic diagnostic)
    {
        if (diagnostics.Count < _maximumDiagnostics - 1)
        {
            diagnostics.Add(diagnostic);
        }
        else if (diagnostics.Count == _maximumDiagnostics - 1)
        {
            diagnostics.Add(ParseDiagnostic.ForInput(
                ParseDiagnosticCode.DiagnosticLimitExceeded,
                "The parse result exceeded the maximum diagnostic count."));
        }
    }

    private static ParseResult InvalidResult(string[] arguments, ParseDiagnostic diagnostic)
    {
        return InvalidResult(arguments, [], [diagnostic], []);
    }

    private static ParseResult InvalidResult(
        string[] arguments,
        CommandDefinition[] selectedPath,
        List<ParseDiagnostic> diagnostics,
        List<UnmatchedToken> unmatched)
    {
        ParseDiagnostic[] orderedDiagnostics = [.. diagnostics
            .OrderBy(static diagnostic => diagnostic.TokenIndex ?? int.MaxValue)
            .ThenBy(static diagnostic => diagnostic.Code.ToString(), StringComparer.Ordinal)
            .Take(_maximumDiagnostics)];
        return new ParseResult(
            ParseOutcome.Invalid,
            selectedPath,
            orderedDiagnostics,
            [.. unmatched],
            arguments,
            []);
    }

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
