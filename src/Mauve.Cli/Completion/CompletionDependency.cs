using System;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Completion;

/// <summary>Identifies one explicitly allowed value dependency for a provider.</summary>
public sealed class CompletionDependency
{
    private CompletionDependency(OptionDefinition? option, PositionalDefinition? positional)
    {
        Option = option;
        Positional = positional;
    }

    /// <summary>Gets the option dependency, when present.</summary>
    public OptionDefinition? Option { get; }

    /// <summary>Gets the positional dependency, when present.</summary>
    public PositionalDefinition? Positional { get; }

    /// <summary>Creates an option dependency.</summary>
    public static CompletionDependency ForOption(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return new CompletionDependency(option, null);
    }

    /// <summary>Creates a positional dependency.</summary>
    public static CompletionDependency ForPositional(PositionalDefinition positional)
    {
        ArgumentNullException.ThrowIfNull(positional);
        return new CompletionDependency(null, positional);
    }
}
