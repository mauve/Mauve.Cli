using System;
using System.Linq;

namespace Mauve.Cli.Definitions;

/// <summary>Configures option prefixes.</summary>
public sealed class CliSyntaxOptions
{
    /// <summary>Creates immutable option syntax settings.</summary>
    /// <param name="longOptionPrefix">The prefix for long option names.</param>
    /// <param name="shortOptionPrefix">The prefix for short option characters.</param>
    public CliSyntaxOptions(
        string longOptionPrefix = "--",
        string shortOptionPrefix = "-")
    {
        ArgumentException.ThrowIfNullOrEmpty(longOptionPrefix);
        ArgumentException.ThrowIfNullOrEmpty(shortOptionPrefix);
        if (longOptionPrefix == shortOptionPrefix
            || shortOptionPrefix.StartsWith(longOptionPrefix, StringComparison.Ordinal)
            || longOptionPrefix.Any(static character => character == '=' || char.IsWhiteSpace(character) || char.IsControl(character))
            || shortOptionPrefix.Any(static character => character == '=' || char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException("Option prefixes must be distinct, inert strings and the long prefix cannot prefix the short prefix.");
        }

        LongOptionPrefix = longOptionPrefix;
        ShortOptionPrefix = shortOptionPrefix;
    }

    /// <summary>Gets the prefix for long option names.</summary>
    public string LongOptionPrefix { get; }

    /// <summary>Gets the prefix for short option characters.</summary>
    public string ShortOptionPrefix { get; }

    /// <summary>Gets the default option syntax.</summary>
    public static CliSyntaxOptions Default { get; } = new();

    internal string FormatLong(string name) => string.Concat(LongOptionPrefix, name);

    internal string FormatShort(char name) => string.Concat(ShortOptionPrefix, name);
}
