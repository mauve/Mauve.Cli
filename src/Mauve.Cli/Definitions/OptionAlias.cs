using System;

namespace Mauve.Cli.Definitions;

/// <summary>An explicitly registered spelling for an option.</summary>
public sealed class OptionAlias
{
    /// <summary>Creates an alias with the supplied spelling and meaning.</summary>
    /// <param name="name">The full alias spelling, such as <c>-o</c>.</param>
    /// <param name="kind">The meaning of the alias.</param>
    public OptionAlias(string name, OptionAliasKind kind = OptionAliasKind.Normal)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Kind = kind;
    }

    /// <summary>Gets the full alias spelling.</summary>
    public string Name { get; }

    /// <summary>Gets the meaning of the alias.</summary>
    public OptionAliasKind Kind { get; }
}
