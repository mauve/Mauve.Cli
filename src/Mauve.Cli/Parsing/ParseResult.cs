using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Parsing;

/// <summary>An immutable parse outcome with diagnostics, coordinates, and typed values.</summary>
public sealed class ParseResult
{
    private readonly ReadOnlyCollection<CommandDefinition> _selectedPath;
    private readonly ReadOnlyCollection<ParseDiagnostic> _diagnostics;
    private readonly ReadOnlyCollection<UnmatchedToken> _unmatchedTokens;
    private readonly ReadOnlyCollection<string> _arguments;
    private readonly ReadOnlyCollection<ParsedValue> _values;

    internal ParseResult(
        ParseOutcome outcome,
        CommandDefinition[] selectedPath,
        ParseDiagnostic[] diagnostics,
        UnmatchedToken[] unmatchedTokens,
        string[] arguments,
        ParsedValue[] values)
    {
        Outcome = outcome;
        _selectedPath = Array.AsReadOnly(selectedPath);
        _diagnostics = Array.AsReadOnly(diagnostics);
        _unmatchedTokens = Array.AsReadOnly(unmatchedTokens);
        _arguments = Array.AsReadOnly(arguments);
        _values = Array.AsReadOnly(values);
    }

    /// <summary>Gets the overall parsing outcome.</summary>
    public ParseOutcome Outcome { get; }

    /// <summary>Gets the selected command, or null when no path was selected.</summary>
    public CommandDefinition? SelectedCommand => _selectedPath.Count == 0 ? null : _selectedPath[^1];

    /// <summary>Gets the canonical selected command path.</summary>
    public IReadOnlyList<CommandDefinition> SelectedPath => _selectedPath;

    /// <summary>Gets diagnostics in deterministic processing order.</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Gets tokens that were not assigned to a definition.</summary>
    public IReadOnlyList<UnmatchedToken> UnmatchedTokens => _unmatchedTokens;

    /// <summary>Gets the defensive copy of the input vector for explicit caller inspection.</summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>Gets values for a valid result; invalid results expose no partial values.</summary>
    public IReadOnlyList<ParsedValue> Values => _values;

    /// <summary>Attempts to get a parsed option value by its owning command and definition.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="owner">The owning command.</param>
    /// <param name="option">The option definition.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetOptionValue<T>(CommandDefinition owner, OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(option);
        EnsureValidated();
        if (!ReferenceEquals(SelectedCommand, owner) || !owner.Options.Contains(option))
        {
            throw new ArgumentException("The option handle is not part of this selected model path.", nameof(option));
        }

        ParsedValue entry = _values.Single(parsed => ReferenceEquals(parsed.Owner, owner) && ReferenceEquals(parsed.Option, option));
        return TryGet(entry, out value);
    }

    /// <summary>Attempts to get a parsed positional value by its owning command and definition.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="owner">The owning command.</param>
    /// <param name="positional">The positional definition.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetPositionalValue<T>(CommandDefinition owner, PositionalDefinition positional, out T? value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(positional);
        EnsureValidated();
        if (!ReferenceEquals(SelectedCommand, owner) || !owner.Positionals.Contains(positional))
        {
            throw new ArgumentException("The positional handle does not belong to the selected command.", nameof(positional));
        }

        ParsedValue entry = _values.Single(parsed => ReferenceEquals(parsed.Owner, owner) && ReferenceEquals(parsed.Positional, positional));
        return TryGet(entry, out value);
    }

    private void EnsureValidated()
    {
        if (Outcome != ParseOutcome.Validated)
        {
            throw new InvalidOperationException("Typed values are available only for a validated parse result.");
        }
    }

    private static bool TryGet<T>(ParsedValue entry, out T? value)
    {
        if (!entry.IsAvailable)
        {
            value = default;
            return false;
        }

        if (entry.Value is T typedValue)
        {
            value = typedValue;
            return true;
        }

        if (entry.Value is null && default(T) is null)
        {
            value = default;
            return true;
        }

        throw new InvalidOperationException("The requested value type does not match the definition.");
    }
}
