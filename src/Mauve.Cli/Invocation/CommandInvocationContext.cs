using System;
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

    /// <summary>Attempts to get a typed option value for the selected command.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="option">The option definition.</param>
    /// <param name="value">The value when available.</param>
    /// <returns>True when the value is available.</returns>
    public bool TryGetOptionValue<T>(OptionDefinition option, out T? value)
    {
        ArgumentNullException.ThrowIfNull(option);
        return ParseResult.TryGetOptionValue(Command, option, out value);
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
}
