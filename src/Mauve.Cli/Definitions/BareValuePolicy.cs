namespace Mauve.Cli.Definitions;

/// <summary>Defines the value assigned to an optional option used without an inline value.</summary>
public enum BareValuePolicy
{
    /// <summary>No bare value behavior is declared.</summary>
    Unspecified,

    /// <summary>The bare occurrence produces a null value.</summary>
    Null,

    /// <summary>The bare occurrence uses a declared constant.</summary>
    Constant,

    /// <summary>The bare occurrence invokes a declared factory.</summary>
    Factory
}
