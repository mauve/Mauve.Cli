using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>A logical completion candidate and the token segment it replaces.</summary>
public sealed class CompletionCandidate
{
    internal CompletionCandidate(
        string insertionText,
        string displayText,
        string? description,
        CompletionCandidateKind kind,
        int replacementStart,
        int replacementLength,
        CommandDefinition? command,
        OptionDefinition? option,
        PositionalDefinition? positional)
    {
        InsertionText = insertionText;
        DisplayText = displayText;
        Description = description;
        Kind = kind;
        ReplacementStart = replacementStart;
        ReplacementLength = replacementLength;
        Command = command;
        Option = option;
        Positional = positional;
    }

    /// <summary>Gets the logical text to insert.</summary>
    public string InsertionText { get; }

    /// <summary>Gets safe display text.</summary>
    public string DisplayText { get; }

    /// <summary>Gets the optional safe description.</summary>
    public string? Description { get; }

    /// <summary>Gets the candidate kind.</summary>
    public CompletionCandidateKind Kind { get; }

    /// <summary>Gets the UTF-16 start offset in the active token.</summary>
    public int ReplacementStart { get; }

    /// <summary>Gets the UTF-16 length replaced in the active token.</summary>
    public int ReplacementLength { get; }

    /// <summary>Gets the command handle for command candidates.</summary>
    public CommandDefinition? Command { get; }

    /// <summary>Gets the option handle for option or option-value candidates.</summary>
    public OptionDefinition? Option { get; }

    /// <summary>Gets the positional handle for positional candidates.</summary>
    public PositionalDefinition? Positional { get; }
}
