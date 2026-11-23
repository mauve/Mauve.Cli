namespace Mauve.Cli.Parsing;

/// <summary>Describes where a parsed value came from.</summary>
public enum ValueOrigin
{
    /// <summary>No value is available.</summary>
    Absent,

    /// <summary>The value came from a declared default or bare-value policy.</summary>
    Default,

    /// <summary>The value came from explicit input.</summary>
    Explicit,

    /// <summary>The value came from an environment variable.</summary>
    Environment
}
