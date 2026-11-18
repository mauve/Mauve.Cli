using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Parsing;

/// <summary>A safe, structured parse diagnostic with original token coordinates.</summary>
public sealed class ParseDiagnostic
{
    private readonly ReadOnlyCollection<string> _definitionPath;
    private readonly ReadOnlyCollection<string> _suggestions;

    internal ParseDiagnostic(
        ParseDiagnosticCode code,
        string message,
        int? tokenIndex,
        int? valueTokenIndex,
        int? valueOffset,
        string definitionName,
        string[] definitionPath,
        string? failureCode,
        string[] suggestions,
        int orderingPhase)
    {
        Code = code;
        Message = message;
        TokenIndex = tokenIndex;
        ValueTokenIndex = valueTokenIndex;
        ValueOffset = valueOffset;
        DefinitionName = definitionName;
        _definitionPath = Array.AsReadOnly(definitionPath);
        FailureCode = failureCode;
        _suggestions = Array.AsReadOnly(suggestions);
        OrderingPhase = orderingPhase;
    }

    /// <summary>Gets the diagnostic category.</summary>
    public ParseDiagnosticCode Code { get; }

    /// <summary>Gets a safe message that does not contain raw input.</summary>
    public string Message { get; }

    /// <summary>Gets the zero-based argv index of the relevant option or token.</summary>
    public int? TokenIndex { get; }

    /// <summary>Gets the zero-based argv index that supplied an invalid value.</summary>
    public int? ValueTokenIndex { get; }

    /// <summary>Gets the UTF-16 offset of an inline value after its first equals sign.</summary>
    public int? ValueOffset { get; }

    /// <summary>Gets the safe definition name or a generic hidden-item label.</summary>
    public string DefinitionName { get; }

    /// <summary>Gets the canonical definition path.</summary>
    public IReadOnlyList<string> DefinitionPath => _definitionPath;

    /// <summary>Gets an application-supplied safe failure identifier, when present.</summary>
    public string? FailureCode { get; }

    /// <summary>Gets bounded visible canonical candidates for unknown names.</summary>
    public IReadOnlyList<string> Suggestions => _suggestions;

    internal int OrderingPhase { get; }

    internal static ParseDiagnostic ForOption(
        ParseDiagnosticCode code,
        OptionDefinition option,
        CommandDefinition owner,
        int? tokenIndex,
        int? valueTokenIndex = null,
        int? valueOffset = null,
        string? failureCode = null)
    {
        string name = option.IsHidden ? "hidden option" : option.Name;
        string message = code switch
        {
            ParseDiagnosticCode.InvalidModel => $"Option definition '{name}' is invalid.",
            ParseDiagnosticCode.MissingValue => $"Option '{name}' requires a value.",
            ParseDiagnosticCode.UnexpectedValue => $"Option '{name}' does not accept that value form.",
            ParseDiagnosticCode.DuplicateOption => $"Option '{name}' cannot be repeated.",
            ParseDiagnosticCode.InvalidValue => $"The value for option '{name}' is invalid.",
            ParseDiagnosticCode.ValidationFailed => $"The value for option '{name}' failed validation.",
            ParseDiagnosticCode.MissingRequired => $"Required option '{name}' is missing.",
            ParseDiagnosticCode.OptionOutOfScope => $"Option '{name}' is not available for the selected command.",
            ParseDiagnosticCode.UnknownCommand => "The token does not name a child command.",
            ParseDiagnosticCode.UnknownOption => "The token does not name a visible option.",
            ParseDiagnosticCode.UnexpectedArgument => "The token cannot be assigned to a positional argument.",
            ParseDiagnosticCode.InputLimitExceeded => "The argument vector exceeds an input limit.",
            ParseDiagnosticCode.DiagnosticLimitExceeded => "The parse result exceeded the maximum diagnostic count.",
            _ => throw new NotImplementedException()
        };
        return new ParseDiagnostic(
            code,
            message,
            tokenIndex,
            valueTokenIndex,
            valueOffset,
            name,
            [.. owner.Path],
            failureCode,
            [],
            GetOrderingPhase(code));
    }

    internal static ParseDiagnostic ForPositional(
        ParseDiagnosticCode code,
        PositionalDefinition positional,
        CommandDefinition owner,
        int? tokenIndex,
        int? valueTokenIndex = null,
        int? valueOffset = null,
        string? failureCode = null)
    {
        string name = positional.IsHidden ? "hidden argument" : positional.Name;
        string message = code switch
        {
            ParseDiagnosticCode.InvalidModel => $"Argument definition '{name}' is invalid.",
            ParseDiagnosticCode.InvalidValue => $"The value for argument '{name}' is invalid.",
            ParseDiagnosticCode.ValidationFailed => $"The value for argument '{name}' failed validation.",
            ParseDiagnosticCode.MissingRequired => $"Required argument '{name}' is missing.",
            ParseDiagnosticCode.MissingValue => $"Argument '{name}' requires a value.",
            ParseDiagnosticCode.UnexpectedValue => $"Argument '{name}' does not accept that value form.",
            ParseDiagnosticCode.DuplicateOption => $"Argument '{name}' cannot be repeated.",
            ParseDiagnosticCode.OptionOutOfScope => $"Argument '{name}' is not available for the selected command.",
            ParseDiagnosticCode.UnknownCommand => "The token does not name a child command.",
            ParseDiagnosticCode.UnknownOption => "The token does not name a visible option.",
            ParseDiagnosticCode.UnexpectedArgument => "The token cannot be assigned to a positional argument.",
            ParseDiagnosticCode.InputLimitExceeded => "The argument vector exceeds an input limit.",
            ParseDiagnosticCode.DiagnosticLimitExceeded => "The parse result exceeded the maximum diagnostic count.",
            _ => throw new NotImplementedException()
        };
        return new ParseDiagnostic(
            code,
            message,
            tokenIndex,
            valueTokenIndex,
            valueOffset,
            name,
            [.. owner.Path],
            failureCode,
            [],
            GetOrderingPhase(code));
    }

    internal static ParseDiagnostic ForInput(
        ParseDiagnosticCode code,
        string message,
        int? tokenIndex = null,
        string? failureCode = null,
        string[]? suggestions = null)
    {
        return new ParseDiagnostic(
            code,
            message,
            tokenIndex,
            null,
            null,
            string.Empty,
            [],
            failureCode,
            suggestions ?? [],
            GetOrderingPhase(code));
    }

    internal static ParseDiagnostic ForCrossValidation(
        CommandDefinition command,
        string failureCode)
    {
        string name = command.IsHidden ? "hidden command" : command.Name;
        return new ParseDiagnostic(
            ParseDiagnosticCode.ValidationFailed,
            $"Values for command '{name}' failed validation.",
            null,
            null,
            null,
            name,
            [.. command.Path],
            failureCode,
            [],
            4);
    }

    private static int GetOrderingPhase(ParseDiagnosticCode code)
    {
        return code switch
        {
            ParseDiagnosticCode.InvalidModel => -1,
            ParseDiagnosticCode.InputLimitExceeded => -1,
            ParseDiagnosticCode.UnknownCommand => 0,
            ParseDiagnosticCode.UnknownOption => 0,
            ParseDiagnosticCode.UnexpectedArgument => 0,
            ParseDiagnosticCode.MissingValue => 0,
            ParseDiagnosticCode.UnexpectedValue => 0,
            ParseDiagnosticCode.DuplicateOption => 0,
            ParseDiagnosticCode.InvalidValue => 1,
            ParseDiagnosticCode.MissingRequired => 2,
            ParseDiagnosticCode.ValidationFailed => 3,
            ParseDiagnosticCode.OptionOutOfScope => 0,
            ParseDiagnosticCode.DiagnosticLimitExceeded => 5,
            _ => 0
        };
    }
}
