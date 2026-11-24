using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Validation;

/// <summary>Read-only values and presence for one validation stage.</summary>
public sealed class ValidationContext
{
    private readonly ReadOnlyCollection<ParsedValue> _values;
    private readonly Dictionary<OptionDefinition, ParsedValue> _options = [];
    private readonly Dictionary<PositionalDefinition, ParsedValue> _positionals = [];

    internal ValidationContext(CommandDefinition command, ParsedValue[] values)
    {
        Command = command;
        _values = Array.AsReadOnly(values);
        foreach (ParsedValue value in values)
        {
            if (value.Option is not null)
            {
                _options.Add(value.Option, value);
            }
            else if (value.Positional is not null)
            {
                _positionals.Add(value.Positional, value);
            }
        }
    }

    /// <summary>Gets the selected command.</summary>
    public CommandDefinition Command { get; }

    /// <summary>Gets read-only value entries assembled for this command.</summary>
    public IReadOnlyList<ParsedValue> Values => _values;

    /// <summary>Gets whether an option was explicitly supplied.</summary>
    /// <param name="option">The selected command's option definition.</param>
    /// <returns>True when the option occurred in argv.</returns>
    public bool WasProvided(OptionDefinition option)
    {
        ArgumentNullException.ThrowIfNull(option);
        ParsedValue value = GetValue(_options, option);
        return value.Presence != ValuePresence.Absent;
    }

    /// <summary>Attempts to get an available option value.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="option">The selected command's option definition.</param>
    /// <param name="value">The available value.</param>
    /// <returns>True when a value is available, including an available null.</returns>
    public bool TryGetOptionValue<T>(OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return GetValue(_options, option).TryGetValue(out value);
    }

    /// <summary>Gets whether a positional argument was explicitly supplied.</summary>
    /// <param name="positional">The selected command's positional definition.</param>
    /// <returns>True when the positional had one or more tokens.</returns>
    public bool WasProvided(PositionalDefinition positional)
    {
        ArgumentNullException.ThrowIfNull(positional);
        ParsedValue value = GetValue(_positionals, positional);
        return value.Presence != ValuePresence.Absent;
    }

    /// <summary>Attempts to get an available positional value.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="positional">The selected command's positional definition.</param>
    /// <param name="value">The available value.</param>
    /// <returns>True when a value is available, including an available null.</returns>
    public bool TryGetPositionalValue<T>(PositionalDefinition positional, out T? value)
    {
        ArgumentNullException.ThrowIfNull(positional);
        return GetValue(_positionals, positional).TryGetValue(out value);
    }

    private static ParsedValue GetValue<TDefinition>(
        Dictionary<TDefinition, ParsedValue> values,
        TDefinition definition)
        where TDefinition : class
    {
        return values.TryGetValue(definition, out ParsedValue? value)
            ? value
            : throw new ArgumentException("The definition does not belong to the selected command.", nameof(definition));
    }

}
