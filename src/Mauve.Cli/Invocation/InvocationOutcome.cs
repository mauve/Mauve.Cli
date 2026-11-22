namespace Mauve.Cli.Invocation;

/// <summary>Describes the result of a high-level command invocation.</summary>
public enum InvocationOutcome
{
    /// <summary>The selected handler completed and supplied an exit code.</summary>
    Completed,

    /// <summary>Parsing or value validation failed; no handler ran.</summary>
    InvalidInput,

    /// <summary>The parser requested help; no handler ran.</summary>
    HelpRequested,

    /// <summary>The selected command has no handler.</summary>
    NoHandler,

    /// <summary>Caller cancellation stopped the invocation.</summary>
    Cancelled,

    /// <summary>The parser requested a help tree; no command handler ran.</summary>
    HelpTreeRequested
}
