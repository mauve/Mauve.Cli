using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

/// <summary>Parses and invokes one selected command without terminating the process.</summary>
public sealed class CliInvoker(CliParser? parser = null)
{
    private readonly CliParser _parser = parser ?? new CliParser();

    /// <summary>Parses and invokes one command, awaiting its handler to completion.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Tokens excluding the executable name.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The invocation outcome and any completed parse result.</returns>
    public async ValueTask<InvocationResult> InvokeAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(arguments);
        if (cancellationToken.IsCancellationRequested)
        {
            return InvocationResult.Cancelled();
        }

        ParseResult parseResult;
        try
        {
            parseResult = _parser.ParseForInvocation(model, arguments, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InvocationResult.Cancelled();
        }

        if (parseResult.Outcome == ParseOutcome.Invalid)
        {
            return InvocationResult.InvalidInput(parseResult);
        }

        if (parseResult.Outcome == ParseOutcome.HelpRequested)
        {
            return InvocationResult.HelpRequested(parseResult);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InvocationResult.Cancelled(parseResult);
        }

        CommandDefinition selectedCommand = parseResult.SelectedCommand
            ?? throw new InvalidOperationException("A validated parse has no selected command.");
        CommandHandler? handler = selectedCommand.Handler;
        if (handler is null)
        {
            return InvocationResult.NoHandler(parseResult);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InvocationResult.Cancelled(parseResult);
        }

        CommandInvocationContext context = new(selectedCommand, parseResult);
        try
        {
            int exitCode = await handler(context, cancellationToken).ConfigureAwait(false);
            return InvocationResult.Completed(parseResult, exitCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InvocationResult.Cancelled(parseResult);
        }
    }
}
