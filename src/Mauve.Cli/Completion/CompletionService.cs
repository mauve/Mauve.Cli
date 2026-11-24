using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;

namespace Mauve.Cli.Completion;

/// <summary>Completes already-tokenized arguments without invoking application handlers.</summary>
public sealed class CompletionService(
    ConverterRegistry? converters = null,
    CultureInfo? numericCulture = null,
    CliSyntaxOptions? syntax = null)
{
    private const int _maximumRawCandidates = 512;
    private const int _maximumEmittedCandidates = 128;
    private const int _maximumCandidateCodeUnits = 1024;
    private readonly ConverterRegistry _converters = converters ?? ConverterRegistry.Empty();
    private readonly CultureInfo _numericCulture = ValueConverterEngine.CreateNumericCulture(numericCulture);
    private readonly CliSyntaxOptions _syntax = syntax ?? CliSyntaxOptions.Default;

    /// <summary>Completes the active token using logical argv tokens and a UTF-16 cursor offset.</summary>
    /// <param name="request">The completion request.</param>
    /// <param name="providers">Optional application-owned value providers.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>A bounded immutable result; provider failures are explicit.</returns>
    public async ValueTask<CompletionResult> CompleteAsync(
        CompletionRequest request,
        CompletionProviderRegistry? providers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        CompletionService configured = request.Model.Converters.IsEmpty
            ? this : new CompletionService(ConverterRegistry.Combine(request.Model.Converters, _converters), _numericCulture, _syntax);
        return await configured.CompleteCoreAsync(request, providers, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CompletionResult> CompleteCoreAsync(
        CompletionRequest request,
        CompletionProviderRegistry? providers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        providers ??= CompletionProviderRegistry.Empty();
        if (cancellationToken.IsCancellationRequested)
        {
            return new CompletionResult(CompletionOutcome.Cancelled, [], false);
        }

        CompletionInput? input = ValidateInput(request);
        if (input is null)
        {
            return new CompletionResult(CompletionOutcome.ContextFailed, [], false, request.CursorTokenIndex);
        }

        CompletionScan scan = ScanCompletedTokens(request.Model, input.CompletedTokens);
        if (scan.FailureTokenIndex is int failureIndex)
        {
            return new CompletionResult(CompletionOutcome.ContextFailed, [], false, failureIndex);
        }

        List<CompletionCandidate> candidates = [];
        HashSet<(string Text, int Start, int Length)> unique = [];
        bool candidateTooLong = false;
        Segment segment = scan.EndOfOptions
            ? new Segment(0, input.ActiveToken.Length, false, false, -1)
            : GetSegment(input.ActiveToken, request.CursorCharacterOffset);
        string prefix = input.ActiveToken[segment.Start..request.CursorCharacterOffset];

        bool activeDirective = input.ActiveToken.StartsWith('[', StringComparison.Ordinal)
            || scan.PendingDirective is not null;
        if (scan.DirectivePhaseOpen
            && !scan.EndOfOptions
            && !segment.IsOptionName
            && !segment.IsOptionValue
            && (activeDirective || input.ActiveToken.Length == 0))
        {
            bool tooLong = AddDirectiveCandidates(
                scan,
                input.ActiveToken,
                prefix,
                request.CursorCharacterOffset,
                segment,
                candidates,
                unique);
            if (activeDirective)
            {
                return LimitOrCompleted(candidates, tooLong);
            }
        }

        if (!scan.EndOfOptions
            && segment.IsOptionName
            && IsOptionPrefix(input.ActiveToken))
        {
            bool negativeNumericValue = scan.Pending is PendingValue activePending
                && ConversionResolver.IsNumericType(activePending.ValueType, _converters)
                && IsNegativeNumericCandidate(input.ActiveToken);
            if (!negativeNumericValue)
            {
                AddOptionCandidates(scan, prefix, segment, input.ActiveToken, candidates, unique,
                    ref candidateTooLong);
                return LimitOrCompleted(candidates, candidateTooLong);
            }
        }

        if (segment.IsOptionValue)
        {
            string optionName = input.ActiveToken[..segment.EqualsIndex];
            CommandOption? option = FindVisibleOption(CommandModel.GetEffectiveOptions(scan.Path), optionName);
            if (option is null || IsNegatingAlias(option.Value.Definition, optionName))
            {
                return new CompletionResult(CompletionOutcome.Completed, [], true);
            }

            if (option.Value.Definition.IsHidden || option.Value.Definition.IsSensitive)
            {
                return new CompletionResult(CompletionOutcome.Completed, [], true);
            }

            if (option.Value.Definition.Arity == OptionArity.BooleanFlag)
            {
                AddValueCandidate("true", option.Value.Definition.Description, prefix, segment, input.ActiveToken,
                    option.Value.Definition, null, candidates, unique, ref candidateTooLong);
                AddValueCandidate("false", option.Value.Definition.Description, prefix, segment, input.ActiveToken,
                    option.Value.Definition, null, candidates, unique, ref candidateTooLong);
                return LimitOrCompleted(candidates, candidateTooLong);
            }

            return option.Value.Definition.Arity == OptionArity.Counting
                ? new CompletionResult(CompletionOutcome.Completed, [], true)
                : await CompleteDefinitionAsync(
                    option.Value.Definition,
                    null,
                    prefix,
                    segment,
                    input.ActiveToken,
                    scan,
                    providers,
                    candidates,
                    unique,
                    cancellationToken).ConfigureAwait(false);
        }

        if (scan.Pending is PendingValue pending)
        {
            if (IsOptionPrefix(input.ActiveToken)
                && !(ConversionResolver.IsNumericType(pending.ValueType, _converters)
                    && IsNegativeNumericCandidate(input.ActiveToken)))
            {
                AddOptionCandidates(scan, prefix, segment, input.ActiveToken, candidates, unique,
                    ref candidateTooLong);
                return LimitOrCompleted(candidates, candidateTooLong);
            }

            if (pending.Option?.Arity == OptionArity.BooleanFlag)
            {
                AddValueCandidate("true", pending.Option.Description, prefix, segment, input.ActiveToken,
                    pending.Option, null, candidates, unique, ref candidateTooLong);
                AddValueCandidate("false", pending.Option.Description, prefix, segment, input.ActiveToken,
                    pending.Option, null, candidates, unique, ref candidateTooLong);
                if (!IsBooleanLiteral(prefix))
                {
                    AddRouteCandidates(scan, prefix, segment, input.ActiveToken, candidates, unique,
                        ref candidateTooLong, allowCommands: true);
                    if (candidateTooLong)
                    {
                        return LimitOrCompleted(candidates, true);
                    }

                    PositionalDefinition? booleanRoutePositional = GetNextPositional(
                        scan.Command,
                        scan.PositionalTokenCount);
                    bool booleanRouteCommandMatched = candidates.Any(static candidate =>
                        candidate.Kind == CompletionCandidateKind.Command);
                    if (booleanRoutePositional is not null
                        && !booleanRoutePositional.IsHidden
                        && !booleanRoutePositional.IsSensitive
                        && (!scan.CommandSelectionOpen
                            || scan.Command.Children.Count == 0
                            || !booleanRouteCommandMatched))
                    {
                        return await CompleteDefinitionAsync(
                            null,
                            booleanRoutePositional,
                            prefix,
                            segment,
                            input.ActiveToken,
                            scan,
                            providers,
                            candidates,
                            unique,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

                return LimitOrCompleted(candidates, candidateTooLong);
            }

            return await CompleteDefinitionAsync(
                pending.Option,
                pending.Positional,
                prefix,
                segment,
                input.ActiveToken,
                scan,
                providers,
                candidates,
                unique,
                cancellationToken).ConfigureAwait(false);
        }

        if (scan.EndOfOptions)
        {
            PositionalDefinition? positional = GetNextPositional(scan.Command, scan.PositionalTokenCount);
            return positional is not null
                ? await CompleteDefinitionAsync(
                    null,
                    positional,
                    prefix,
                    segment,
                    input.ActiveToken,
                    scan,
                    providers,
                    candidates,
                    unique,
                    cancellationToken).ConfigureAwait(false)
                : new CompletionResult(CompletionOutcome.Completed, [], true);
        }

        AddRouteCandidates(scan, prefix, segment, input.ActiveToken, candidates, unique,
            ref candidateTooLong, allowCommands: true);
        if (candidateTooLong)
        {
            return LimitOrCompleted(candidates, true);
        }

        PositionalDefinition? nextPositional = GetNextPositional(scan.Command, scan.PositionalTokenCount);
        bool commandCandidateMatched = candidates.Any(static candidate =>
            candidate.Kind == CompletionCandidateKind.Command);
        return nextPositional is not null
            && !nextPositional.IsHidden
            && !nextPositional.IsSensitive
            && (!scan.CommandSelectionOpen
                || scan.Command.Children.Count == 0
                || !commandCandidateMatched)
            ? await CompleteDefinitionAsync(
                null,
                nextPositional,
                prefix,
                segment,
                input.ActiveToken,
                scan,
                providers,
                candidates,
                unique,
                cancellationToken).ConfigureAwait(false)
            : LimitOrCompleted(candidates, candidates.Count > _maximumEmittedCandidates);
    }

    /// <summary>Returns an explicit unsupported outcome for shell-specific completion requests.</summary>
    /// <param name="shell">The shell adapter name requested by the host.</param>
    /// <param name="request">The logical-token completion request.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>A core completion result or an unsupported-shell outcome.</returns>
    public static ValueTask<CompletionResult> CompleteForShellAsync(
        string shell,
        CompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(request);
        return shell.Length == 0
            ? throw new ArgumentException("A shell name cannot be empty.", nameof(shell))
            : ValueTask.FromResult(new CompletionResult(
            cancellationToken.IsCancellationRequested ? CompletionOutcome.Cancelled : CompletionOutcome.UnsupportedShell,
            [],
            false));
    }

    private async ValueTask<CompletionResult> CompleteDefinitionAsync(
        OptionDefinition? option,
        PositionalDefinition? positional,
        string prefix,
        Segment segment,
        string activeToken,
        CompletionScan scan,
        CompletionProviderRegistry providers,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique,
        CancellationToken cancellationToken)
    {
        if (option?.IsHidden == true
            || option?.IsSensitive == true
            || positional?.IsHidden == true
            || positional?.IsSensitive == true)
        {
            return new CompletionResult(CompletionOutcome.Completed, [], true);
        }

        object definition = option ?? (object?)positional
            ?? throw new InvalidOperationException("A completion target has no definition.");
        IReadOnlyList<string> allowedValues = option?.AllowedValues ?? positional!.AllowedValues;
        if (allowedValues.Count > 0)
        {
            bool candidateTooLong = false;
            int inspected = 0;
            foreach (string value in allowedValues)
            {
                if (inspected++ == _maximumRawCandidates)
                {
                    return LimitOrCompleted(candidates, true);
                }

                AddValueCandidate(value, option?.Description ?? positional?.Description, prefix, segment, activeToken,
                    option, positional, candidates, unique, ref candidateTooLong);
                if (candidateTooLong || candidates.Count > _maximumEmittedCandidates)
                {
                    return LimitOrCompleted(candidates, true);
                }
            }

            return LimitOrCompleted(candidates, candidateTooLong);
        }

        if (providers.TryGet(definition, out ProviderRegistration registration))
        {
            if (!DependenciesAreVisible(registration.Dependencies, scan))
            {
                return new CompletionResult(CompletionOutcome.ContextFailed, candidates, false);
            }

            CompletionContext context = new(prefix, scan.Path, scan.Values, registration.Dependencies);
            return await ReadProviderAsync(
                registration,
                context,
                prefix,
                segment,
                activeToken,
                option,
                positional,
                candidates,
                unique,
                cancellationToken).ConfigureAwait(false);
        }

        Type? valueType = option?.ElementType ?? positional?.ElementType ?? option?.ValueType ?? positional?.ValueType;
        if (valueType is not null && option?.Converter is null && positional?.Converter is null)
        {
            bool candidateTooLong = false;
            string? description = option?.Description ?? positional?.Description;
            foreach (string name in _converters.GetEnumNames(valueType))
            {
                AddValueCandidate(name, description, prefix, segment, activeToken,
                    option, positional, candidates, unique, ref candidateTooLong);
            }

            return LimitOrCompleted(candidates, candidateTooLong);
        }

        return new CompletionResult(CompletionOutcome.Completed, candidates, true);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Provider, enumerator and cleanup faults must become safe ProviderFailed outcomes.")]
    private static async ValueTask<CompletionResult> ReadProviderAsync(
        ProviderRegistration registration,
        CompletionContext context,
        string prefix,
        Segment segment,
        string activeToken,
        OptionDefinition? option,
        PositionalDefinition? positional,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<CompletionValue>? sequence;
        try
        {
            sequence = registration.Provider(context, cancellationToken);
            if (sequence is null)
            {
                return new CompletionResult(CompletionOutcome.ProviderFailed, candidates, false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CompletionResult(CompletionOutcome.Cancelled, candidates, false);
        }
        catch
        {
            return new CompletionResult(CompletionOutcome.ProviderFailed, candidates, false);
        }

        IAsyncEnumerator<CompletionValue>? enumerator = null;
        Exception? enumerationFault = null;
        Exception? cleanupFault = null;
        CompletionOutcome outcome = CompletionOutcome.Completed;
        int rawCount = 0;
        try
        {
            enumerator = sequence.GetAsyncEnumerator(cancellationToken);
            while (rawCount < _maximumRawCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    break;
                }

                rawCount++;
                CompletionValue? value = enumerator.Current;
                if (value is null
                    || value.InsertionText.Length + value.DisplayText.Length + (value.Description?.Length ?? 0)
                    > _maximumCandidateCodeUnits)
                {
                    outcome = CompletionOutcome.LimitReached;
                    break;
                }

                if (!value.InsertionText.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string display = HelpTextSanitizer.SingleLine(value.DisplayText);
                string? description = value.Description is null
                    ? null
                    : HelpTextSanitizer.Description(value.Description);
                _ = AddCandidate(
                    value.InsertionText,
                    display,
                    description,
                    option is null ? CompletionCandidateKind.Positional : CompletionCandidateKind.Value,
                    segment,
                    activeToken,
                    null,
                    option,
                    positional,
                    candidates,
                    unique);
                if (candidates.Count > _maximumEmittedCandidates)
                {
                    outcome = CompletionOutcome.LimitReached;
                    break;
                }
            }

            if (outcome == CompletionOutcome.Completed && rawCount == _maximumRawCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    outcome = CompletionOutcome.LimitReached;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = CompletionOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            enumerationFault = exception;
            outcome = CompletionOutcome.ProviderFailed;
        }
        finally
        {
            if (enumerator is not null)
            {
                try
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFault = exception;
                }
            }
        }

        if (enumerationFault is not null || cleanupFault is not null)
        {
            outcome = CompletionOutcome.ProviderFailed;
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            outcome = CompletionOutcome.Cancelled;
        }

        return new CompletionResult(
            outcome,
            candidates.Count > _maximumEmittedCandidates
                ? candidates.Take(_maximumEmittedCandidates)
                : candidates,
            outcome == CompletionOutcome.Completed);
    }

    private static bool DependenciesAreVisible(
        IReadOnlyList<CompletionDependency> dependencies,
        CompletionScan scan)
    {
        IReadOnlyList<CommandOption> options = CommandModel.GetEffectiveOptions(scan.Path);
        foreach (CompletionDependency dependency in dependencies)
        {
            if (dependency.Option is not null
                && !options.Any(candidate => ReferenceEquals(candidate.Definition, dependency.Option)))
            {
                return false;
            }

            if (dependency.Positional is not null
                && (!ReferenceEquals(scan.Command.Positionals.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate, dependency.Positional)), dependency.Positional)))
            {
                return false;
            }
        }

        return true;
    }

    private void AddRouteCandidates(
        CompletionScan scan,
        string prefix,
        Segment segment,
        string activeToken,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique,
        ref bool candidateTooLong,
        bool allowCommands)
    {
        if (allowCommands && scan.CommandSelectionOpen && !IsOptionPrefix(prefix))
        {
            foreach (CommandDefinition child in scan.Command.Children)
            {
                if (child.IsHidden)
                {
                    continue;
                }

                if (child.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    candidateTooLong |= AddCandidate(child.Name, HelpTextSanitizer.SingleLine(child.Name),
                    HelpTextSanitizer.DescriptionOrNull(child.Description), CompletionCandidateKind.Command,
                    segment, activeToken, child, null, null, candidates, unique);
                }
                foreach (string alias in child.Aliases)
                {
                    if (alias.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        candidateTooLong |= AddCandidate(alias, HelpTextSanitizer.SingleLine(alias),
                            HelpTextSanitizer.DescriptionOrNull(child.Description), CompletionCandidateKind.Command,
                            segment, activeToken, child, null, null, candidates, unique);
                    }
                }
            }
        }

        if (IsOptionPrefix(prefix))
        {
            AddOptionCandidates(scan, prefix, segment, activeToken, candidates, unique, ref candidateTooLong);
            return;
        }

        if (scan.CommandSelectionOpen && scan.Command.Children.Count > 0 && scan.Command.Positionals.Count == 0)
        {
            return;
        }

        PositionalDefinition? positional = GetNextPositional(scan.Command, scan.PositionalTokenCount);
        if (positional is not null && !positional.IsHidden && !positional.IsSensitive)
        {
        }
    }

    private void AddOptionCandidates(
        CompletionScan scan,
        string prefix,
        Segment segment,
        string activeToken,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique,
        ref bool candidateTooLong)
    {
        foreach (CommandOption commandOption in CommandModel.GetEffectiveOptions(scan.Path))
        {
            OptionDefinition option = commandOption.Definition;
            if (option.IsHidden || option.IsSensitive)
            {
                continue;
            }

            if (option.LongName is string longName)
            {
                string longSpelling = _syntax.FormatLong(longName);
                if (longSpelling.StartsWith(prefix, StringComparison.Ordinal))
                {
                    candidateTooLong |= AddCandidate(longSpelling, HelpTextSanitizer.SingleLine(longSpelling),
                        HelpTextSanitizer.DescriptionOrNull(option.Description), CompletionCandidateKind.Option,
                        segment, activeToken, null, option, null, candidates, unique);
                }
            }
            foreach (OptionAlias alias in option.Aliases)
            {
                string aliasSpelling = _syntax.FormatLong(alias.Name);
                if (!option.IsHidden && !option.IsSensitive
                    && aliasSpelling.StartsWith(prefix, StringComparison.Ordinal))
                {
                    candidateTooLong |= AddCandidate(aliasSpelling, HelpTextSanitizer.SingleLine(aliasSpelling),
                        HelpTextSanitizer.DescriptionOrNull(option.Description), CompletionCandidateKind.Option,
                        segment, activeToken, null, option, null, candidates, unique);
                }
            }

            if (!option.IsHidden && !option.IsSensitive)
            {
                if (option.ShortName is char primaryShort)
                {
                    string shortSpelling = _syntax.FormatShort(primaryShort);
                    if (shortSpelling.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        candidateTooLong |= AddCandidate(shortSpelling, HelpTextSanitizer.SingleLine(shortSpelling),
                            HelpTextSanitizer.DescriptionOrNull(option.Description), CompletionCandidateKind.Option,
                            segment, activeToken, null, option, null, candidates, unique);
                    }
                }

                foreach (char aliasShort in option.ShortAliases)
                {
                    string shortSpelling = _syntax.FormatShort(aliasShort);
                    if (shortSpelling.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        candidateTooLong |= AddCandidate(shortSpelling, HelpTextSanitizer.SingleLine(shortSpelling),
                            HelpTextSanitizer.DescriptionOrNull(option.Description), CompletionCandidateKind.Option,
                            segment, activeToken, null, option, null, candidates, unique);
                    }
                }
            }
        }
    }

    private static void AddValueCandidate(
        string value,
        string? description,
        string prefix,
        Segment segment,
        string activeToken,
        OptionDefinition? option,
        PositionalDefinition? positional,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique,
        ref bool candidateTooLong)
    {
        if (value.StartsWith(prefix, StringComparison.Ordinal))
        {
            candidateTooLong |= AddCandidate(
                value,
                HelpTextSanitizer.SingleLine(value),
                HelpTextSanitizer.DescriptionOrNull(description),
                positional is null ? CompletionCandidateKind.Value : CompletionCandidateKind.Positional,
                segment, activeToken, null, option, positional, candidates, unique);
        }
    }

    private static bool AddCandidate(
        string insertionText,
        string displayText,
        string? description,
        CompletionCandidateKind kind,
        Segment segment,
        string activeToken,
        CommandDefinition? command,
        OptionDefinition? option,
        PositionalDefinition? positional,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique)
    {
        if (insertionText.Length + displayText.Length + (description?.Length ?? 0) > _maximumCandidateCodeUnits)
        {
            return true;
        }

        (string Text, int Start, int Length) key = (insertionText, segment.Start, segment.Length(activeToken));
        if (!unique.Add(key))
        {
            return false;
        }

        candidates.Add(new CompletionCandidate(
            insertionText,
            displayText,
            description,
            kind,
            segment.Start,
            segment.Length(activeToken),
            command,
            option,
            positional));
        return false;
    }

    private static CompletionResult LimitOrCompleted(
        List<CompletionCandidate> candidates,
        bool reachedBound)
    {
        bool limited = reachedBound || candidates.Count > _maximumEmittedCandidates;
        if (candidates.Count > _maximumEmittedCandidates)
        {
            candidates.RemoveRange(_maximumEmittedCandidates, candidates.Count - _maximumEmittedCandidates);
        }

        return new CompletionResult(
            limited ? CompletionOutcome.LimitReached : CompletionOutcome.Completed,
            candidates,
            !limited);
    }

    private CompletionScan ScanCompletedTokens(CommandModel model, string[] tokens)
    {
        CompletionScan scan = new(model);
        List<CommandOption> occurrences = [];
        List<(PositionalDefinition Definition, string Token, int Index)> positionals = [];
        OptionDefinition? pending = null;
        int pendingIndex = -1;
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            if (pending?.Arity == OptionArity.BooleanFlag)
            {
                if (TryBoolean(token, out bool pendingBoolean))
                {
                    StoreSafeValue(scan, pending, pendingBoolean);
                    pending = null;
                    continue;
                }

                pending = null;
            }

            if (pending?.Arity == OptionArity.GreedyCollection
                && (token == "--"
                    || (IsOptionCandidate(token)
                        && !(ConversionResolver.IsNumericType(pending.ElementType!, _converters)
                            && IsNegativeNumericCandidate(token)))))
            {
                pending = null;
            }

            if (pending is not null && pending.Arity != OptionArity.GreedyCollection)
            {
                Type pendingType = pending.ElementType ?? pending.ValueType;
                if (!CanConsumeValue(token, pendingType))
                {
                    scan.FailureTokenIndex = pendingIndex;
                    return scan;
                }

                if (!pending.IsHidden && !pending.IsSensitive)
                {
                    if (!TryConvertValues(pendingType, [token], pending.Name, index,
                        false, false, pending.Converter, out object? pendingValue))
                    {
                        scan.FailureTokenIndex = index;
                        return scan;
                    }

                    StoreOptionValue(scan, pending, pendingValue, false);
                }

                pending = null;
                continue;
            }

            if (!scan.EndOfOptions && token == "--")
            {
                scan.EndOfOptions = true;
                scan.CommandSelectionOpen = false;
                scan.DirectivePhaseOpen = false;
                continue;
            }

            if (!scan.EndOfOptions
                && scan.DirectivePhaseOpen
                && token.StartsWith('[', StringComparison.Ordinal))
            {
                if (!ScanDirectiveSpan(model, tokens, ref index, scan))
                {
                    scan.FailureTokenIndex = index;
                    return scan;
                }

                continue;
            }

            if (!scan.EndOfOptions && IsOptionCandidate(token))
            {
                int equals = token.IndexOf('=', StringComparison.Ordinal);
                string name = equals < 0 ? token : token[..equals];
                if (name.StartsWith(_syntax.ShortOptionPrefix, StringComparison.Ordinal)
                    && name.Length > _syntax.ShortOptionPrefix.Length + 1
                    && !name.StartsWith(_syntax.LongOptionPrefix, StringComparison.Ordinal))
                {
                    if (!ScanShortCluster(equals, name, occurrences, scan))
                    {
                        scan.FailureTokenIndex = index;
                        return scan;
                    }

                    continue;
                }

                CommandOption? match = FindVisibleOption(CommandModel.GetEffectiveOptions(scan.Path), name);
                if (match is null)
                {
                    scan.FailureTokenIndex = index;
                    return scan;
                }

                OptionDefinition option = match.Value.Definition;
                if (IsNegatingAlias(option, name) && equals >= 0)
                {
                    scan.FailureTokenIndex = index;
                    return scan;
                }

                if (!occurrences.Exists(occurrence => ReferenceEquals(occurrence.Definition, option))
                    && option.Arity is not (OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection or OptionArity.Counting))
                {
                    occurrences.Add(match.Value);
                }
                else if (option.Arity is not (OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection or OptionArity.Counting))
                {
                    scan.FailureTokenIndex = index;
                    return scan;
                }

                if (IsNegatingAlias(option, name))
                {
                    StoreSafeValue(scan, option, false);
                    continue;
                }

                if (option.Arity == OptionArity.Counting)
                {
                    if (equals >= 0)
                    {
                        scan.FailureTokenIndex = index;
                        return scan;
                    }

                    int count = scan.Values.TryGetValue(option, out object? existingCount) && existingCount is int currentCount
                        ? currentCount
                        : 0;
                    StoreSafeValue(scan, option, checked(count + 1));
                    continue;
                }

                if (option.Arity == OptionArity.BooleanFlag)
                {
                    if (equals >= 0)
                    {
                        string boolText = token[(equals + 1)..];
                        if (!TryBoolean(boolText, out bool boolValue))
                        {
                            scan.FailureTokenIndex = index;
                            return scan;
                        }

                        StoreSafeValue(scan, option, boolValue);
                    }
                    else if (index + 1 < tokens.Length && TryBoolean(tokens[index + 1], out bool nextBool))
                    {
                        index++;
                        StoreSafeValue(scan, option, nextBool);
                    }
                    else
                    {
                        StoreSafeValue(scan, option, true);
                        pending = option;
                    }

                    continue;
                }

                if (option.Arity == OptionArity.OptionalScalar && equals < 0)
                {
                    continue;
                }

                List<string> rawValues = [];
                if (equals >= 0)
                {
                    rawValues.Add(token[(equals + 1)..]);
                }
                else if (option.Arity == OptionArity.GreedyCollection)
                {
                    while (index + 1 < tokens.Length
                        && CanConsumeValue(tokens[index + 1], option.ElementType!))
                    {
                        rawValues.Add(tokens[++index]);
                    }
                }
                else if (index + 1 < tokens.Length
                    && CanConsumeValue(tokens[index + 1], option.ElementType ?? option.ValueType))
                {
                    rawValues.Add(tokens[++index]);
                }

                if (rawValues.Count == 0)
                {
                    pending = option;
                    pendingIndex = index;
                    continue;
                }

                bool isCollection = option.Arity is OptionArity.RepeatedSingleCollection or OptionArity.GreedyCollection;
                if (option.IsHidden || option.IsSensitive)
                {
                    pending = option.Arity == OptionArity.GreedyCollection ? option : null;
                    continue;
                }

                if (!TryConvertValues(option.ElementType ?? option.ValueType, rawValues, option.Name,
                    index, false, isCollection, option.Converter, out object? converted))
                {
                    scan.FailureTokenIndex = index;
                    return scan;
                }

                StoreOptionValue(scan, option, converted, isCollection);

                pending = option.Arity == OptionArity.GreedyCollection ? option : null;
                continue;
            }

            if (scan.CommandSelectionOpen && scan.Command.TryGetChild(token, out CommandDefinition? child))
            {
                scan.Path.Add(child);
                scan.Command = child;
                scan.DirectivePhaseOpen = false;
                continue;
            }

            if (scan.CommandSelectionOpen && scan.Command.Children.Count > 0 && scan.Command.Positionals.Count == 0)
            {
                scan.FailureTokenIndex = index;
                return scan;
            }

            PositionalDefinition? positional = GetNextPositional(scan.Command, scan.PositionalTokenCount);
            if (positional is null)
            {
                scan.FailureTokenIndex = index;
                return scan;
            }

            positionals.Add((positional, token, index));
            scan.PositionalTokenCount++;
            scan.CommandSelectionOpen = false;
            scan.DirectivePhaseOpen = false;
        }

        scan.Pending = pending is null ? null : new PendingValue(pending, null,
            pending.ElementType ?? pending.ValueType);
        ConvertPositionals(scan, positionals);
        return scan;
    }

    private bool AddDirectiveCandidates(
        CompletionScan scan,
        string activeToken,
        string prefix,
        int cursor,
        Segment segment,
        List<CompletionCandidate> candidates,
        HashSet<(string Text, int Start, int Length)> unique)
    {
        DirectiveDefinition? activeDirective = scan.PendingDirective;
        int valueIndex = scan.PendingDirectiveValueIndex;
        int colonIndex = activeToken.IndexOf(':', StringComparison.Ordinal);
        if (activeDirective is null
            && activeToken.StartsWith('[', StringComparison.Ordinal)
            && colonIndex >= 0
            && cursor > colonIndex)
        {
            int closing = activeToken.IndexOf(']', StringComparison.Ordinal);
            string name = activeToken[1..(closing >= 0 && closing < colonIndex ? closing : colonIndex)];
            if (scan.Command is not null)
            {
                _ = scan.RootModel.TryGetDirective(name, out activeDirective);
            }
            valueIndex = 0;
        }

        if (activeDirective is not null)
        {
            if (valueIndex >= activeDirective.Values.Count)
            {
                return false;
            }

            DirectiveValueDefinition definition = activeDirective.Values[valueIndex];
            if (definition.IsHidden || definition.IsSensitive)
            {
                return false;
            }

            Type valueType = definition.ElementType ?? definition.ValueType;
            string valuePrefix = scan.PendingDirective is not null
                ? prefix
                : activeToken[(colonIndex + 1)..Math.Min(cursor, activeToken.Length)];
            bool limited = false;
            foreach (string value in definition.Converter is null ? _converters.GetEnumNames(valueType) : [])
            {
                if (!value.StartsWith(valuePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string insertion = scan.PendingDirective is not null
                    ? string.Concat(value, activeToken.Length > 0 && activeToken[^1] == ']' ? string.Empty : "]")
                    : string.Concat(activeToken[..(colonIndex + 1)], value, "]");
                limited |= AddCandidate(
                    insertion,
                    HelpTextSanitizer.SingleLine(value),
                    HelpTextSanitizer.DescriptionOrNull(definition.Description),
                    CompletionCandidateKind.Value,
                    segment,
                    activeToken,
                    null,
                    null,
                    null,
                    candidates,
                    unique);
            }

            return limited;
        }

        string directivePrefix = activeToken.StartsWith('[', StringComparison.Ordinal)
            ? activeToken[1..Math.Min(cursor, activeToken.Length)]
            : prefix;
        int nameEnd = directivePrefix.IndexOfAny([':', ']']);
        if (nameEnd >= 0)
        {
            directivePrefix = directivePrefix[..nameEnd];
        }

        bool candidateTooLong = false;
        foreach (DirectiveDefinition directive in scan.RootModel.Directives)
        {
            if (directive.IsHidden || !directive.Name.StartsWith(directivePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            string insertionText = string.Concat("[", directive.Name, "]");
            candidateTooLong |= AddCandidate(
                insertionText,
                insertionText,
                HelpTextSanitizer.DescriptionOrNull(directive.Description),
                CompletionCandidateKind.Directive,
                segment,
                activeToken,
                null,
                null,
                null,
                candidates,
                unique);
        }

        return candidateTooLong;
    }

    private static bool ScanDirectiveSpan(
        CommandModel model,
        string[] tokens,
        ref int index,
        CompletionScan scan)
    {
        string header = tokens[index][1..];
        int colon = header.IndexOf(':', StringComparison.Ordinal);
        int close = header.IndexOf(']', StringComparison.Ordinal);
        bool hasColon = colon >= 0 && (close < 0 || colon < close);
        int nameLength = hasColon ? colon : close >= 0 ? close : header.Length;
        string name = header[..nameLength];
        if (!model.TryGetDirective(name, out DirectiveDefinition? directive)
            || name.Length == 0 || (close >= 0 && close != header.Length - 1))
        {
            return false;
        }

        int valueCount = 0;
        if (hasColon)
        {
            int valueStart = colon + 1;
            int valueEnd = close >= 0 ? close : header.Length;
            if (valueEnd > valueStart || (close >= 0 && valueEnd == valueStart))
            {
                valueCount++;
            }
        }

        bool closed = close >= 0;
        while (!closed && index + 1 < tokens.Length)
        {
            index++;
            string token = tokens[index];
            int tokenClose = token.IndexOf(']', StringComparison.Ordinal);
            if (tokenClose >= 0)
            {
                if (tokenClose != token.Length - 1)
                {
                    return false;
                }

                if (tokenClose > 0)
                {
                    valueCount++;
                }

                closed = true;
            }
            else
            {
                valueCount++;
            }
        }

        if (!closed)
        {
            scan.PendingDirective = directive;
            scan.PendingDirectiveValueIndex = valueCount;
            return true;
        }

        return valueCount <= directive.Values.Count
            && (directive.Values.Count == 0 || valueCount == 0 || directive.Values[^1].ElementType is not null);
    }

    private void ConvertPositionals(
        CompletionScan scan,
        List<(PositionalDefinition Definition, string Token, int Index)> values)
    {
        foreach (IGrouping<PositionalDefinition, (PositionalDefinition Definition, string Token, int Index)> group
            in values.GroupBy(static value => value.Definition, ReferenceEqualityComparer.Instance).Cast<IGrouping<PositionalDefinition, (PositionalDefinition Definition, string Token, int Index)>>())
        {
            PositionalDefinition definition = group.Key;
            if (definition.IsHidden || definition.IsSensitive)
            {
                continue;
            }

            (PositionalDefinition Definition, string Token, int Index)[] inputs = [.. group];
            if (!TryConvertValues(definition.ElementType ?? definition.ValueType,
                [.. inputs.Select(static input => input.Token)],
                definition.Name,
                inputs[0].Index,
                false,
                definition.MaximumValues is null or > 1,
                definition.Converter,
                out object? converted))
            {
                scan.FailureTokenIndex = inputs[0].Index;
                return;
            }

            if (!definition.IsHidden && !definition.IsSensitive && converted is not null)
            {
                scan.Values[definition] = converted;
            }
        }
    }

    private bool TryConvertValues(
        Type type,
        IReadOnlyList<string> values,
        string name,
        int tokenIndex,
        bool isSensitive,
        bool asCollection,
        ValueConverterRegistration? converter,
        out object? converted)
    {
        List<object?> items = [];
        foreach (string value in values)
        {
            if (!ConversionResolver.IsCompletionSafe(type, _converters, converter))
            {
                converted = null;
                return true;
            }

            ConversionContext context = new(type, name, tokenIndex, _numericCulture, isSensitive);
            if (!ConversionResolver.TryConvert(value, type, context, _converters, converter, out object? item, out _))
            {
                converted = null;
                return false;
            }

            items.Add(item);
        }

        if (!asCollection)
        {
            converted = items[0];
            return true;
        }

        converted = ConversionResolver.CreateReadOnlyCollection(type, items, _converters, converter);
        return true;
    }

    private bool ScanShortCluster(
        int equalsIndex,
        string optionName,
        List<CommandOption> occurrences,
        CompletionScan scan)
    {
        if (equalsIndex >= 0)
        {
            return false;
        }

        string characters = optionName[_syntax.ShortOptionPrefix.Length..];
        List<CommandOption> resolved = [];
        HashSet<OptionDefinition> withinCluster = [];
        IReadOnlyList<CommandOption> visible = CommandModel.GetEffectiveOptions(scan.Path);
        foreach (char character in characters)
        {
            CommandOption? match = FindVisibleOption(visible, string.Concat(_syntax.ShortOptionPrefix, character));
            if (match is null)
            {
                return false;
            }

            OptionDefinition option = match.Value.Definition;
            if (option.Arity is not (OptionArity.BooleanFlag or OptionArity.Counting or OptionArity.OptionalScalar)
                || (option.Arity != OptionArity.Counting
                    && (occurrences.Exists(occurrence => ReferenceEquals(occurrence.Definition, option))
                        || !withinCluster.Add(option))))
            {
                return false;
            }

            resolved.Add(match.Value);
        }

        foreach (CommandOption item in resolved)
        {
            OptionDefinition option = item.Definition;
            if (option.Arity != OptionArity.Counting)
            {
                occurrences.Add(item);
            }

            if (option.Arity == OptionArity.BooleanFlag)
            {
                StoreSafeValue(scan, option, true);
            }
            else if (option.Arity == OptionArity.Counting)
            {
                int count = scan.Values.TryGetValue(option, out object? existingCount) && existingCount is int currentCount
                    ? currentCount
                    : 0;
                StoreSafeValue(scan, option, checked(count + 1));
            }
            else if (!option.IsHidden && !option.IsSensitive
                && option.BareValuePolicy == BareValuePolicy.Constant)
            {
                scan.Values[option] = option.BareValue;
            }
        }
        return true;
    }

    private static void StoreSafeValue(CompletionScan scan, OptionDefinition option, object value)
    {
        if (!option.IsHidden && !option.IsSensitive)
        {
            scan.Values[option] = value;
        }
    }

    private void StoreOptionValue(
        CompletionScan scan,
        OptionDefinition option,
        object? converted,
        bool isCollection)
    {
        if (option.IsHidden || option.IsSensitive || converted is null)
        {
            return;
        }

        if (!isCollection)
        {
            scan.Values[option] = converted;
            return;
        }

        List<object?> values = [];
        if (scan.Values.TryGetValue(option, out object? existing) && existing is IEnumerable existingValues)
        {
            foreach (object? value in existingValues)
            {
                values.Add(value);
            }
        }

        if (converted is IEnumerable convertedValues)
        {
            foreach (object? value in convertedValues)
            {
                values.Add(value);
            }
        }

        scan.Values[option] = ConversionResolver.CreateReadOnlyCollection(
            option.ElementType!,
            values,
            _converters,
            option.Converter);
    }

    private static CompletionInput? ValidateInput(CompletionRequest request)
    {
        IReadOnlyList<string> arguments = request.Arguments;
        int tokenCount = arguments.Count;
        CliLimits limits = request.Model.Limits;
        if (request.CursorTokenIndex < 0 || request.CursorTokenIndex > tokenCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The cursor token index is outside the argument vector.");
        }

        if (request.CursorTokenIndex == tokenCount && request.CursorCharacterOffset != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The append cursor offset must be zero.");
        }

        if (tokenCount > limits.MaximumArgumentCount)
        {
            return null;
        }

        string[] tokens = new string[tokenCount];
        long totalLength = 0;
        for (int index = 0; index < tokenCount; index++)
        {
            string? token = arguments[index] ?? throw new ArgumentNullException(string.Create(CultureInfo.InvariantCulture, $"arguments[{index}]"));
            if (token.Length > limits.MaximumTokenLength)
            {
                return null;
            }

            totalLength += token.Length;
            if (totalLength > limits.MaximumTotalCodeUnits)
            {
                return null;
            }

            tokens[index] = token;
        }

        string activeToken = request.CursorTokenIndex == tokenCount
            ? string.Empty
            : tokens[request.CursorTokenIndex];
        if (request.CursorCharacterOffset < 0 || request.CursorCharacterOffset > activeToken.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The cursor offset is outside the active token.");
        }

        string[] completedTokens = tokens[..request.CursorTokenIndex];
        return new CompletionInput(completedTokens, activeToken);
    }

    private Segment GetSegment(string token, int cursor)
    {
        int equalsIndex = token.IndexOf('=', StringComparison.Ordinal);
        return equalsIndex >= 0
            && IsOptionCandidate(token[..equalsIndex])
            ? cursor <= equalsIndex
                ? new Segment(0, equalsIndex, true, false, equalsIndex)
                : new Segment(equalsIndex + 1, token.Length - equalsIndex - 1, false, true, equalsIndex)
            : new Segment(0, token.Length, IsOptionPrefix(token), false, -1);
    }

    private CommandOption? FindVisibleOption(IReadOnlyList<CommandOption> options, string name)
    {
        string longName = name.StartsWith(_syntax.LongOptionPrefix, StringComparison.Ordinal)
            ? name[_syntax.LongOptionPrefix.Length..]
            : name;
        bool shortForm = name.StartsWith(_syntax.ShortOptionPrefix, StringComparison.Ordinal)
            && name.Length == _syntax.ShortOptionPrefix.Length + 1;
        char shortName = shortForm ? name[^1] : '\0';
        foreach (CommandOption commandOption in options)
        {
            if ((!shortForm && (string.Equals(commandOption.Definition.LongName, longName, StringComparison.Ordinal)
                || commandOption.Definition.Aliases.Any(alias => string.Equals(alias.Name, longName, StringComparison.Ordinal))))
                || (shortForm && (commandOption.Definition.ShortName == shortName
                    || commandOption.Definition.ShortAliases.Contains(shortName))))
            {
                return commandOption;
            }
        }

        return null;
    }

    private bool IsNegatingAlias(OptionDefinition option, string name)
    {
        string longName = name.StartsWith(_syntax.LongOptionPrefix, StringComparison.Ordinal)
            ? name[_syntax.LongOptionPrefix.Length..]
            : name;
        return option.Aliases.Any(alias =>
            alias.Kind == OptionAliasKind.Negating
            && string.Equals(alias.Name, longName, StringComparison.Ordinal));
    }

    private static PositionalDefinition? GetNextPositional(CommandDefinition command, int tokenCount)
    {
        int consumed = 0;
        foreach (PositionalDefinition positional in command.Positionals)
        {
            int maximum = positional.MaximumValues ?? int.MaxValue;
            if (tokenCount < consumed + maximum || positional.MaximumValues is null)
            {
                return positional;
            }

            consumed += maximum;
        }

        return null;
    }

    private bool CanConsumeValue(string token, Type valueType)
    {
        return token != "--"
            && (!IsOptionCandidate(token)
                || (ConversionResolver.IsNumericType(valueType, _converters) && IsNegativeNumericCandidate(token)));
    }

    private bool IsOptionCandidate(string token)
    {
        return (token.Length > _syntax.LongOptionPrefix.Length
                && token.StartsWith(_syntax.LongOptionPrefix, StringComparison.Ordinal))
            || (token.Length > _syntax.ShortOptionPrefix.Length
                && token.StartsWith(_syntax.ShortOptionPrefix, StringComparison.Ordinal));
    }

    private bool IsOptionPrefix(string token)
    {
        return token.StartsWith(_syntax.LongOptionPrefix, StringComparison.Ordinal)
            || token.StartsWith(_syntax.ShortOptionPrefix, StringComparison.Ordinal);
    }

    private static bool IsNegativeNumericCandidate(string token)
    {
        return token.Length > 1
            && token[0] == '-'
            && (char.IsAsciiDigit(token[1])
                || (token.Length > 2 && token[1] == '.' && char.IsAsciiDigit(token[2])));
    }

    private static bool TryBoolean(string token, out bool value)
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

    private static bool IsBooleanLiteral(string token)
    {
        return token.Equals("true", StringComparison.OrdinalIgnoreCase)
            || token.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CompletionInput(string[] CompletedTokens, string ActiveToken);

    private sealed record PendingValue(
        OptionDefinition? Option,
        PositionalDefinition? Positional,
        Type ValueType);

    private sealed class CompletionScan(CommandModel model)
    {
        public CommandModel RootModel { get; } = model;

        public CommandDefinition Command { get; set; } = model.Root;

        public List<CommandDefinition> Path { get; } = [model.Root];

        public Dictionary<object, object?> Values { get; } =
            [with(ReferenceEqualityComparer.Instance)];

        public bool CommandSelectionOpen { get; set; } = true;

        public bool DirectivePhaseOpen { get; set; } = true;

        public bool EndOfOptions { get; set; }

        public DirectiveDefinition? PendingDirective { get; set; }

        public int PendingDirectiveValueIndex { get; set; }

        public int PositionalTokenCount { get; set; }

        public int? FailureTokenIndex { get; set; }

        public PendingValue? Pending { get; set; }

    }

    private readonly record struct Segment(
        int Start,
        int InitialLength,
        bool IsOptionName,
        bool IsOptionValue,
        int EqualsIndex)
    {
        public int Length(string token)
        {
            return Math.Max(0, IsOptionValue
                ? token.Length - Start
                : Math.Min(InitialLength, token.Length - Start));
        }
    }
}
