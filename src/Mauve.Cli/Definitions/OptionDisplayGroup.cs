using System;

namespace Mauve.Cli.Definitions;

/// <summary>Defines stable grouping metadata for an option in generated help.</summary>
public sealed class OptionDisplayGroup(string key, string title, string? description = null)
{
    /// <summary>Gets the stable group identity, independent of its display title.</summary>
    public string Key { get; } = Validate(key, nameof(key));

    /// <summary>Gets the display title.</summary>
    public string Title { get; } = title ?? throw new ArgumentNullException(nameof(title));

    /// <summary>Gets the optional group description.</summary>
    public string? Description { get; } = description;

    private static string Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length is 0 or > 128)
        {
            throw new ArgumentException("A display group key must contain between 1 and 128 characters.", parameterName);
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-' or '/'))
            {
                throw new ArgumentException("A display group key contains an invalid character.", parameterName);
            }
        }

        return value;
    }
}
