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
        => Run(TCommand.CreateModel(includeStandardHelp: true), arguments, options, cancellationToken);

    /// <summary>Runs a synchronous generated root with additional synchronous directives.</summary>
    /// <typeparam name="TCommand">A synchronous generated root with a builder.</typeparam>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="directives">Additional root directive builders.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or configured host outcome code.</returns>
    public static int Run<TCommand>(
        IReadOnlyList<string>? arguments,
        IReadOnlyList<DirectiveBuilder> directives,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ISynchronousGeneratedCliCommand, IGeneratedCliCommandBuilder
        => Run(CreateModel(TCommand.CreateBuilder(), directives, requireSynchronous: true), arguments, options, cancellationToken);

    /// <summary>Runs a model whose handlers complete synchronously.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or the configured host outcome code.</returns>
    public static int Run(
        CommandModel model,
        IReadOnlyList<string>? arguments = null,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        CliHostOptions hostOptions = options ?? new CliHostOptions();
        ValueTask<InvocationResult> invocation = new CliInvoker(new CliParser(hostOptions.Converters, hostOptions.NumericCulture, hostOptions.Syntax))
            .InvokeCoreAsync(model, ResolveArguments(arguments), hostOptions, cancellationToken);
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
        => RunAsync(TCommand.CreateModel(includeStandardHelp: true), arguments, options, cancellationToken);

    /// <summary>Runs a generated root with additional root directives.</summary>
    /// <typeparam name="TCommand">A generated root with a builder.</typeparam>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="directives">Additional root directive builders.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or configured host outcome code.</returns>
    public static Task<int> RunAsync<TCommand>(
        IReadOnlyList<string>? arguments,
        IReadOnlyList<DirectiveBuilder> directives,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : IGeneratedCliCommandBuilder
        => RunAsync(CreateModel(TCommand.CreateBuilder(), directives, requireSynchronous: false), arguments, options, cancellationToken);

    /// <summary>Runs an explicitly supplied command model.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="options">Optional host output and exit-code settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or the configured host outcome code.</returns>
    public static async Task<int> RunAsync(
        CommandModel model,
        IReadOnlyList<string>? arguments = null,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        CliHostOptions hostOptions = options ?? new CliHostOptions();
        InvocationResult result = await new CliInvoker(new CliParser(hostOptions.Converters, hostOptions.NumericCulture, hostOptions.Syntax))
            .InvokeCoreAsync(model, ResolveArguments(arguments), hostOptions, cancellationToken)
            .ConfigureAwait(false);
        return WriteHostResult(model, result, hostOptions);
    }

    /// <summary>Parses and invokes one command, awaiting its handler to completion.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Tokens excluding the executable name.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The invocation outcome and any completed parse result.</returns>
    public ValueTask<InvocationResult> InvokeAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
        => InvokeCoreAsync(model, arguments, new CliHostOptions(), cancellationToken);

    private async ValueTask<InvocationResult> InvokeCoreAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CliHostOptions hostOptions,
        CancellationToken cancellationToken)
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

        if (!parseResult.DirectiveInputValid)
        {
            return InvocationResult.InvalidInput(parseResult);
        }

        foreach (ParsedDirective directive in parseResult.Directives)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return InvocationResult.Cancelled(parseResult);
            }

            DirectiveHandler directiveHandler = directive.Definition.Handler
                ?? throw new InvalidOperationException("A validated directive has no handler.");
            DirectiveInvocationContext directiveContext = new(model, directive, parseResult, hostOptions);
            try
            {
                DirectiveResult directiveResult = await directiveHandler(directiveContext, cancellationToken).ConfigureAwait(false);
                if (!directiveResult.ShouldContinue)
                {
                    return InvocationResult.CompletedByDirective(parseResult, directiveResult.ExitCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return InvocationResult.Cancelled(parseResult);
            }
        }

        if (parseResult.Outcome == ParseOutcome.Invalid)
        {
            return InvocationResult.InvalidInput(parseResult);
        }

        if (parseResult.Outcome == ParseOutcome.HelpRequested)
        {
            return InvocationResult.HelpRequested(parseResult);
        }

        if (parseResult.Outcome == ParseOutcome.HelpTreeRequested)
        {
            return InvocationResult.HelpTreeRequested(parseResult);
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

    private static CommandModel CreateModel(
        CommandBuilder root,
        IReadOnlyList<DirectiveBuilder> directives,
        bool requireSynchronous)
    {
        ArgumentNullException.ThrowIfNull(directives);
        foreach (DirectiveBuilder directive in directives)
        {
            ArgumentNullException.ThrowIfNull(directive);
            if (requireSynchronous && directive.IsAsync)
            {
                throw new ArgumentException("Asynchronous directives require RunAsync.", nameof(directives));
            }

            _ = root.AddDirective(directive);
        }

        return CommandModel.Create(root, includeStandardHelp: true);
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
                    HelpDocument.Create(model, selectedCommand, options.ExecutableName, options.Syntax),
                    options.StandardOutput.WriteLine,
                    options.HelpWidth,
                    options.HelpRenderer);
                return 0;
            case InvocationOutcome.HelpTreeRequested:
                CommandDefinition treeCommand = result.SelectedCommand
                    ?? throw new InvalidOperationException("A help-tree request has no selected command.");
                HelpTreeOutput.Write(
                    HelpTreeDocument.Create(model, treeCommand),
                    options.StandardOutput.WriteLine,
                    options.HelpTreeRenderer);
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
