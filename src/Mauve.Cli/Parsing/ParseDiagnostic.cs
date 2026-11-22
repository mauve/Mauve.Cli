using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Encodings.Web;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;

namespace Mauve.Cli.Parsing;

/// <summary>A structured parse diagnostic with original token coordinates.</summary>
public sealed class ParseDiagnostic
{
    private readonly ReadOnlyCollection<string> _definitionPath;
    private readonly ReadOnlyCollection<string> _suggestions;
    private readonly string? _unexpectedArgumentText;

    private ParseDiagnostic(
        ParseDiagnosticCode code,
        string message,
        int? tokenIndex,
        int? valueTokenIndex,
        int? valueOffset,
        string definitionName,
        string[] definitionPath,
        string? failureCode,
        string[] suggestions,
        int orderingPhase,
        string? unexpectedArgumentText = null)
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
        _unexpectedArgumentText = unexpectedArgumentText;
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

    /// <summary>Formats the diagnostic, including escaped text for an unexpected positional argument.</summary>
    /// <returns>Human-readable diagnostic text that may contain user input.</returns>
    public override string ToString() => ToString(includeArgumentText: true);

    /// <summary>Formats the diagnostic with a one-based argument position and visible suggestions, when available.</summary>
    /// <param name="includeArgumentText">Whether to quote escaped unexpected positional input. Use false when output may be logged.</param>
    /// <returns>Human-readable diagnostic text.</returns>
    public string ToString(bool includeArgumentText)
    {
        string message = includeArgumentText && _unexpectedArgumentText is string argument
            ? $"Unexpected positional argument \"{EscapeArgument(argument)}\"."
            : Message;
        int? index = ValueTokenIndex ?? TokenIndex;
        string text = index is int tokenIndex
            ? string.Create(CultureInfo.InvariantCulture, $"Argument {tokenIndex + 1L}: {message}")
            : message;
        return Suggestions.Count == 0
            ? text
            : $"{text} Did you mean: {string.Join(", ", Suggestions)}?";
    }

    internal int OrderingPhase { get; }

    internal static ParseDiagnostic ForOption(
        ParseDiagnosticCode code,
        OptionDefinition option,
        CommandDefinition owner,
        int? tokenIndex,
        int? valueTokenIndex = null,
        int? valueOffset = null,
        string? failureCode = null,
        CommandDefinition? selectedCommand = null)
    {
        string name = option.IsHidden ? "hidden option" : option.Name;
        string message = code switch
        {
            ParseDiagnosticCode.InvalidModel => $"Option '{name}' has an unsupported value type or converter.",
            ParseDiagnosticCode.MissingValue => $"Option '{name}' requires a value.",
            ParseDiagnosticCode.UnexpectedValue => $"The negating form of option '{name}' does not accept a value.",
            ParseDiagnosticCode.DuplicateOption => $"Option '{name}' cannot be repeated.",
            ParseDiagnosticCode.InvalidValue => $"The value for option '{name}' is invalid.",
            ParseDiagnosticCode.ValidationFailed => $"The value for option '{name}' failed validation.",
            ParseDiagnosticCode.MissingRequired => WithCommand($"Required option '{name}' is missing.", selectedCommand ?? owner),
            ParseDiagnosticCode.OptionOutOfScope => $"Option '{name}' is not available for the selected command.",
            ParseDiagnosticCode.UnknownCommand or ParseDiagnosticCode.UnknownOption
                or ParseDiagnosticCode.UnknownDirective or ParseDiagnosticCode.MalformedDirective
                or ParseDiagnosticCode.MissingDirectiveValue or ParseDiagnosticCode.InvalidDirectiveValue
                or ParseDiagnosticCode.ConflictingOptions
                or ParseDiagnosticCode.UnexpectedArgument
                or ParseDiagnosticCode.InputLimitExceeded or ParseDiagnosticCode.DiagnosticLimitExceeded =>
                throw new ArgumentException("Expected an option diagnostic.", nameof(code)),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not an option diagnostic.")
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
            ParseDiagnosticCode.InvalidModel => $"Argument '{name}' has an unsupported value type or converter.",
            ParseDiagnosticCode.InvalidValue => $"The value for argument '{name}' is invalid.",
            ParseDiagnosticCode.ValidationFailed => $"The value for argument '{name}' failed validation.",
            ParseDiagnosticCode.MissingRequired => WithCommand($"Required argument '{name}' is missing.", owner),
            ParseDiagnosticCode.UnknownCommand or ParseDiagnosticCode.UnknownOption
                or ParseDiagnosticCode.UnknownDirective or ParseDiagnosticCode.MalformedDirective
                or ParseDiagnosticCode.MissingDirectiveValue or ParseDiagnosticCode.InvalidDirectiveValue
                or ParseDiagnosticCode.ConflictingOptions
                or ParseDiagnosticCode.UnexpectedArgument
                or ParseDiagnosticCode.MissingValue or ParseDiagnosticCode.UnexpectedValue or ParseDiagnosticCode.DuplicateOption
                or ParseDiagnosticCode.OptionOutOfScope or ParseDiagnosticCode.InputLimitExceeded or ParseDiagnosticCode.DiagnosticLimitExceeded =>
                throw new ArgumentException("Expected a positional diagnostic.", nameof(code)),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a positional diagnostic.")
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

    internal static ParseDiagnostic ForUnexpectedArgument(UnmatchedToken token)
    {
        return new ParseDiagnostic(
            ParseDiagnosticCode.UnexpectedArgument,
            "Unexpected positional argument.",
            token.Index,
            null,
            null,
            string.Empty,
            [],
            null,
            [],
            GetOrderingPhase(ParseDiagnosticCode.UnexpectedArgument),
            token.Value);
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
        string name = command.IsHidden ? "hidden command" : HelpTextSanitizer.SingleLine(command.Name);
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
            ParseDiagnosticCode.UnknownDirective => 0,
            ParseDiagnosticCode.MalformedDirective => 0,
            ParseDiagnosticCode.MissingDirectiveValue => 0,
            ParseDiagnosticCode.InvalidDirectiveValue => 1,
            ParseDiagnosticCode.UnexpectedArgument => 0,
            ParseDiagnosticCode.MissingValue => 0,
            ParseDiagnosticCode.UnexpectedValue => 0,
            ParseDiagnosticCode.DuplicateOption => 0,
            ParseDiagnosticCode.ConflictingOptions => 0,
            ParseDiagnosticCode.InvalidValue => 1,
            ParseDiagnosticCode.MissingRequired => 2,
            ParseDiagnosticCode.ValidationFailed => 3,
            ParseDiagnosticCode.OptionOutOfScope => 0,
            ParseDiagnosticCode.DiagnosticLimitExceeded => 5,
            _ => 0
        };
    }

    private static string WithCommand(string message, CommandDefinition command) => command.IsHidden ? message : $"Command '{HelpTextSanitizer.SingleLine(command.Name)}': {message}";

    private static string EscapeArgument(string argument)
    {
        const int maximumLength = 128;
        if (argument.Length <= maximumLength)
        {
            return JavaScriptEncoder.Default.Encode(argument);
        }

        int length = char.IsHighSurrogate(argument[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
        return string.Concat(JavaScriptEncoder.Default.Encode(argument[..length]), "...");
    }
}
