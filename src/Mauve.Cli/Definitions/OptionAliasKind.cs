namespace Mauve.Cli.Definitions;

/// <summary>Describes the meaning of an option alias.</summary>
public enum OptionAliasKind
{
    /// <summary>The alias has the normal meaning of its option.</summary>
    Normal,

    /// <summary>The alias explicitly negates a boolean flag.</summary>
    Negating
}
