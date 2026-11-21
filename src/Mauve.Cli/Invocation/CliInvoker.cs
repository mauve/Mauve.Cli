using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Parsing;

namespace Mauve.Cli.Invocation;

/// <summary>Parses and invokes one selected command without terminating the process.</summary>
public sealed class CliInvoker(CliParser? parser = null)
{
    private readonly CliParser _parser = parser ?? new CliParser();

    /// <summary>Runs a synchronous generated command root using process arguments by default.</summary>
    /// <typeparam name="TCommand">A generated root whose complete command tree is synchronous.</typeparam>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or the configured host outcome code.</returns>
    public static int Run<TCommand>(
        IReadOnlyList<string>? arguments = null,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ISynchronousGeneratedCliCommand
    {
        CommandModel model = TCommand.CreateModel(includeStandardHelp: true);
        CliHostOptions hostOptions = options ?? new CliHostOptions();
        ValueTask<InvocationResult> invocation = new CliInvoker()
            .InvokeAsync(model, ResolveArguments(arguments), cancellationToken);
        if (!invocation.IsCompleted)
        {
            throw new InvalidOperationException("A synchronous generated command did not complete synchronously.");
        }

        InvocationResult result = invocation.GetAwaiter().GetResult();
        return WriteHostResult(model, result, hostOptions);
    }

    /// <summary>Runs any generated command root using process arguments by default.</summary>
    /// <typeparam name="TCommand">A generated command root.</typeparam>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>A task producing the selected handler's exit code or configured host outcome code.</returns>
    public static Task<int> RunAsync<TCommand>(
        IReadOnlyList<string>? arguments = null,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : IGeneratedCliCommand
        => RunAsyncCore<TCommand>(arguments, options, cancellationToken);

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

    private static async Task<int> RunAsyncCore<TCommand>(
        IReadOnlyList<string>? arguments,
        CliHostOptions? options,
        CancellationToken cancellationToken)
        where TCommand : IGeneratedCliCommand
    {
        CommandModel model = TCommand.CreateModel(includeStandardHelp: true);
        CliHostOptions hostOptions = options ?? new CliHostOptions();
        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ResolveArguments(arguments), cancellationToken)
            .ConfigureAwait(false);
        return WriteHostResult(model, result, hostOptions);
    }

    private static IReadOnlyList<string> ResolveArguments(IReadOnlyList<string>? arguments)
    {
        if (arguments is not null)
        {
            return arguments;
        }

        string[] processArguments = Environment.GetCommandLineArgs();
        return processArguments[1..];
    }

    private static int WriteHostResult(
        CommandModel model,
        InvocationResult result,
        CliHostOptions options)
    {
        switch (result.Outcome)
        {
            case InvocationOutcome.Completed:
                return result.ExitCode
                    ?? throw new InvalidOperationException("A completed invocation has no exit code.");
            case InvocationOutcome.HelpRequested:
                CommandDefinition selectedCommand = result.SelectedCommand
                    ?? throw new InvalidOperationException("A help request has no selected command.");
                HelpOutput.Write(
                    HelpDocument.Create(model, selectedCommand),
                    options.StandardOutput.WriteLine,
                    options.HelpWidth,
                    options.HelpRenderer);
                return 0;
            case InvocationOutcome.InvalidInput:
                foreach (string line in result.FormatDiagnostics())
                {
                    options.StandardError.WriteLine(line);
                }

                return options.InvalidInputExitCode;
            case InvocationOutcome.NoHandler:
                options.StandardError.WriteLine("The selected command has no handler.");
                return options.NoHandlerExitCode;
            case InvocationOutcome.Cancelled:
                return options.CancelledExitCode;
            default:
                throw new InvalidOperationException("The invocation returned an unsupported outcome.");
        }
    }
}
