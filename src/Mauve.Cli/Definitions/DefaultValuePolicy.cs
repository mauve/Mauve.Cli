namespace Mauve.Cli.Definitions;

/// <summary>Defines how a definition obtains a value when it is absent.</summary>
public enum DefaultValuePolicy
{
    /// <summary>No absent value is available.</summary>
    None,

    /// <summary>Use an immutable typed constant.</summary>
    Constant,

    /// <summary>Call a fresh-value factory for each parse.</summary>
    Factory
}
