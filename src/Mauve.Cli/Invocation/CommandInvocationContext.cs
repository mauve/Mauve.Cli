using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

internal delegate ValueTask<int> CommandHandler(
    CommandInvocationContext context,
    CancellationToken cancellationToken);

/// <summary>Provides typed values for one validated command invocation.</summary>
public sealed class CommandInvocationContext
{
    internal CommandInvocationContext(CommandDefinition command, ParseResult parseResult)
    {
        Command = command;
        ParseResult = parseResult;
    }

    /// <summary>Gets the selected command.</summary>
    public CommandDefinition Command { get; }

    /// <summary>Gets the validated parse result for this invocation.</summary>
    public ParseResult ParseResult { get; }

    /// <summary>Gets directive occurrences that completed before the command handler.</summary>
    public IReadOnlyList<ParsedDirective> Directives => ParseResult.Directives;

    /// <summary>Attempts to get a typed option value for the selected command.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="option">The option definition.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetOptionValue<T>(OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return ParseResult.TryGetOptionValue(option, out value);
    }

    /// <summary>Attempts to get a typed positional value for the selected command.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="positional">The positional definition.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetPositionalValue<T>(PositionalDefinition positional, out T? value)
    {
        ArgumentNullException.ThrowIfNull(positional);
        return ParseResult.TryGetPositionalValue(Command, positional, out value);
    }

    /// <summary>Gets an option-pack view included by the selected command.</summary>
    /// <param name="key">The top-level pack inclusion key.</param>
    /// <returns>The invocation-local pack view.</returns>
    public OptionPackContext GetOptionPack(string key) => GetOptionPack(Command, key);

    /// <summary>Gets an option-pack view included by a command on the selected path.</summary>
    /// <param name="owner">The command that declared the inclusion.</param>
    /// <param name="key">The top-level pack inclusion key.</param>
    /// <returns>The invocation-local pack view.</returns>
    public OptionPackContext GetOptionPack(CommandDefinition owner, string key)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(key);
        if (!ParseResult.SelectedPath.Contains(owner))
        {
            throw new ArgumentException("The pack owner is not part of the selected command path.", nameof(owner));
        }

        OptionPackSnapshot? pack = owner.OptionPacks.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return pack is null
            ? throw new ArgumentException("The pack key does not belong to the owning command.", nameof(key))
            : new OptionPackContext(ParseResult, owner, pack);
    }
}
