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

    /// <summary>Prepends the configured long option prefix to a name.</summary>
    /// <param name="name">The name to append; it is not validated or normalized.</param>
    /// <returns>The configured prefix followed by the name.</returns>
    public string FormatLong(string name) => string.Concat(LongOptionPrefix, name);

    /// <summary>Prepends the configured short option prefix to a character.</summary>
    /// <param name="name">The character to append; it is not validated or normalized.</param>
    /// <returns>The configured prefix followed by the character.</returns>
    public string FormatShort(char name) => string.Concat(ShortOptionPrefix, name);
}
