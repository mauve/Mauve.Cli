namespace Mauve.Cli.Parsing;

/// <summary>Describes the overall parsing outcome.</summary>
public enum ParseOutcome
{
    /// <summary>The input was parsed and validated.</summary>
    Validated,

    /// <summary>A help request was recognized without producing dispatchable values.</summary>
    HelpRequested,

    /// <summary>The input or model is invalid.</summary>
    Invalid,

    /// <summary>A help-tree request was recognized without producing dispatchable values.</summary>
    HelpTreeRequested
}
