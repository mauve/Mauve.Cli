namespace Mauve.Cli.Completion;

/// <summary>Identifies the kind of completion candidate.</summary>
public enum CompletionCandidateKind
{
    /// <summary>A visible child command.</summary>
    Command,

    /// <summary>A visible option spelling.</summary>
    Option,

    /// <summary>A value candidate.</summary>
    Value,

    /// <summary>A positional value candidate.</summary>
    Positional
}
