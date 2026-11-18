using System;
using System.Collections.Generic;

namespace Mauve.Cli.Definitions;

/// <summary>Builds a command graph that can be validated into an immutable model.</summary>
public sealed class CommandBuilder
{
    private readonly List<string> _aliases = [];
    private readonly List<CommandBuilder> _children = [];
    private readonly List<OptionDefinition> _options = [];
    private readonly List<PositionalDefinition> _positionals = [];

    /// <summary>Creates a command builder.</summary>
    /// <param name="name">The canonical name or root display label.</param>
    public CommandBuilder(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the canonical name or root display label.</summary>
    public string Name { get; }

    /// <summary>Gets or sets the short description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the long description.</summary>
    public string LongDescription { get; set; } = string.Empty;

    /// <summary>Gets or sets whether ordinary help and completion hide this command.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Adds an alias to this command.</summary>
    /// <param name="alias">The command alias.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        _aliases.Add(alias);
        return this;
    }

    /// <summary>Adds a child command.</summary>
    /// <param name="child">The child builder.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddChild(CommandBuilder child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.Add(child);
        return this;
    }

    /// <summary>Adds an option definition.</summary>
    /// <param name="option">The option definition.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddOption(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        _options.Add(option);
        return this;
    }

    /// <summary>Adds a positional argument definition.</summary>
    /// <param name="positional">The positional argument definition.</param>
    /// <returns>This builder.</returns>
    public CommandBuilder AddPositional(PositionalDefinition positional)
    {
        ArgumentNullException.ThrowIfNull(positional);
        _positionals.Add(positional);
        return this;
    }

    internal string[] CopyAliases()
    {
        return [.. _aliases];
    }

    internal CommandBuilder[] CopyChildren()
    {
        return [.. _children];
    }

    internal OptionDefinition[] CopyOptions()
    {
        return [.. _options];
    }

    internal PositionalDefinition[] CopyPositionals()
    {
        return [.. _positionals];
    }
}
