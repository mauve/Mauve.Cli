using System;
using System.Collections.Generic;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

/// <summary>Contains an invocation outcome and its parse result, when available.</summary>
public sealed class InvocationResult
{
    private InvocationResult(InvocationOutcome outcome, ParseResult? parseResult, int? exitCode)
    {
        Outcome = outcome;
        ParseResult = parseResult;
        ExitCode = exitCode;
    }

    /// <summary>Gets the invocation outcome.</summary>
    public InvocationOutcome Outcome { get; }

    /// <summary>Returns true if the help was requested during invocation.</summary>
    public bool IsHelpRequested => Outcome == InvocationOutcome.HelpRequested;

    /// <summary>Returns true if the invocation resulted in invalid input.</summary>
    public bool IsInvalidInput => Outcome == InvocationOutcome.InvalidInput;

    /// <summary>Gets the parse result when parsing completed.</summary>
    public ParseResult? ParseResult { get; }

    /// <summary>Gets the selected command definition when parsing completed.</summary>
    public CommandDefinition? SelectedCommand => ParseResult?.SelectedCommand;

    /// <summary>Gets the handler's exact exit code when it completed.</summary>
    public int? ExitCode { get; }

    /// <summary>Formats the diagnostics from the parse result, if available.</summary>
    public IEnumerable<string> FormatDiagnostics(bool includeArgumentText = true) => ParseResult?.FormatDiagnostics(includeArgumentText) ?? [];

    internal static InvocationResult Completed(ParseResult parseResult, int exitCode)
    {
        RequireOutcome(parseResult, ParseOutcome.Validated);
        return new InvocationResult(InvocationOutcome.Completed, parseResult, exitCode);
    }

    internal static InvocationResult CompletedByDirective(ParseResult parseResult, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return parseResult.DirectiveInputValid
            ? new InvocationResult(InvocationOutcome.Completed, parseResult, exitCode)
            : throw new InvalidOperationException("An invalid directive plan cannot complete an invocation.");
    }

    internal static InvocationResult InvalidInput(ParseResult parseResult)
    {
        RequireOutcome(parseResult, ParseOutcome.Invalid);
        return new InvocationResult(InvocationOutcome.InvalidInput, parseResult, null);
    }

    internal static InvocationResult HelpRequested(ParseResult parseResult)
    {
        RequireOutcome(parseResult, ParseOutcome.HelpRequested);
        return new InvocationResult(InvocationOutcome.HelpRequested, parseResult, null);
    }

    internal static InvocationResult NoHandler(ParseResult parseResult)
    {
        RequireOutcome(parseResult, ParseOutcome.Validated);
        return new InvocationResult(InvocationOutcome.NoHandler, parseResult, null);
    }

    internal static InvocationResult Cancelled(ParseResult? parseResult = null) => new(InvocationOutcome.Cancelled, parseResult, null);

    private static void RequireOutcome(ParseResult parseResult, ParseOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (parseResult.Outcome != expected)
        {
            throw new InvalidOperationException("The parse result does not match the invocation outcome.");
        }
    }
}
