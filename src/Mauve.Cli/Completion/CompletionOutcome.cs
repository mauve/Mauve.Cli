namespace Mauve.Cli.Completion;

/// <summary>Identifies the result of a completion request.</summary>
public enum CompletionOutcome
{
    /// <summary>Completion completed, including when there are no matches.</summary>
    Completed,

    /// <summary>The caller cancelled completion.</summary>
    Cancelled,

    /// <summary>Completed input could not be resolved against the model.</summary>
    ContextFailed,

    /// <summary>An application provider or its enumerator failed.</summary>
    ProviderFailed,

    /// <summary>A candidate or work bound was reached.</summary>
    LimitReached,

    /// <summary>The request named a shell adapter not supported by the core.</summary>
    UnsupportedShell
}
