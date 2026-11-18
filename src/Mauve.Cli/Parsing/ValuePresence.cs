namespace Mauve.Cli.Parsing;

/// <summary>Describes whether and how a value was supplied.</summary>
public enum ValuePresence
{
    /// <summary>No occurrence was supplied.</summary>
    Absent,

    /// <summary>The option occurred without an inline value.</summary>
    Bare,

    /// <summary>A value token or equals value was supplied.</summary>
    WithValue
}
