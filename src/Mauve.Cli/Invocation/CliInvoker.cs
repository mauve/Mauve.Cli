using System;
using System.Collections.Generic;
using System.IO;
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
        ValueTask<HostInvocationResult> invocation = new CliInvoker(new CliParser(hostOptions.Converters, hostOptions.NumericCulture, hostOptions.Syntax))
            .InvokeForHostAsync(model, ResolveArguments(arguments), hostOptions, cancellationToken);
        if (!invocation.IsCompleted)
        {
            throw new InvalidOperationException("A synchronous generated command did not complete synchronously.");
        }

        HostInvocationResult result = invocation.GetAwaiter().GetResult();
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
        HostInvocationResult result = await new CliInvoker(new CliParser(hostOptions.Converters, hostOptions.NumericCulture, hostOptions.Syntax))
            .InvokeForHostAsync(model, ResolveArguments(arguments), hostOptions, cancellationToken)
            .ConfigureAwait(false);
        return WriteHostResult(model, result, hostOptions);
    }

    /// <summary>Dispatches a multicall executable to a model root or an explicitly mapped command path.</summary>
    /// <param name="models">Validated models whose root names and aliases are implicit executable routes.</param>
    /// <param name="routes">Additional executable names mapped to full command paths.</param>
    /// <param name="arguments">Optional command-line tokens, excluding the executable name.</param>
    /// <param name="options">Host settings whose executable name defaults to process argv zero.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The selected handler's exit code or configured host outcome code.</returns>
    /// <remarks>
    /// Windows ignores executable-name casing and a trailing .exe; other platforms use exact ordinal names.
    /// Command path segments always use ordinal names or aliases. The mapped path is not inserted into argv.
    /// All routes must be valid and have unique normalized executable names before any invocation begins.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required collection or collection item is null.</exception>
    /// <exception cref="ArgumentException">Models are empty, a route path is invalid, or executable routes collide.</exception>
    /// <exception cref="InvalidOperationException">The executable name does not match a route.</exception>
    public static async Task<int> DispatchMulticallAsync(
        IReadOnlyList<CommandModel> models,
        IReadOnlyList<CliMulticallRoute>? routes = null,
        IReadOnlyList<string>? arguments = null,
        CliHostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(models);
        if (models.Count == 0)
        {
            throw new ArgumentException("Multicall dispatch requires at least one command model.", nameof(models));
        }

        CliHostOptions hostOptions = options ?? new CliHostOptions();
        Dictionary<string, MulticallTarget> targets =
            [with(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)];
        foreach (CommandModel model in models)
        {
            ArgumentNullException.ThrowIfNull(model);
            MulticallTarget root = new(model, [model.Root]);
            AddMulticallTarget(targets, model.Root.Name, root);
            foreach (string alias in model.Root.Aliases)
            {
                AddMulticallTarget(targets, alias, root);
            }
        }

        if (routes is not null)
        {
            foreach (CliMulticallRoute route in routes)
            {
                ArgumentNullException.ThrowIfNull(route);
                AddMulticallTarget(targets, route.ExecutableName, ResolveMulticallPath(models, route));
            }
        }

        string executable = NormalizeMulticallName(Path.GetFileName(hostOptions.ExecutableName));
        if (!targets.TryGetValue(executable, out MulticallTarget? target))
        {
            throw new InvalidOperationException("The executable name does not match any configured multicall route.");
        }

        HostInvocationResult result = await new CliInvoker(new CliParser(hostOptions.Converters, hostOptions.NumericCulture, hostOptions.Syntax))
            .InvokeForHostAsync(target.Model, ResolveArguments(arguments), hostOptions, cancellationToken, target.Path)
            .ConfigureAwait(false);
        return WriteHostResult(target.Model, result, hostOptions, target.Path.Count);
    }

    private static void AddMulticallTarget(
        Dictionary<string, MulticallTarget> targets,
        string name,
        MulticallTarget target)
    {
        if (name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A multicall executable route must identify a single filename.");
        }

        string normalized = NormalizeMulticallName(name);
        if (normalized.Length == 0 || !targets.TryAdd(normalized, target))
        {
            throw new ArgumentException("Multicall routes must have nonempty, unique platform-normalized executable names.");
        }
    }

    private static string NormalizeMulticallName(string name)
        => OperatingSystem.IsWindows() && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;

    private static MulticallTarget ResolveMulticallPath(
        IReadOnlyList<CommandModel> models,
        CliMulticallRoute route)
    {
        CommandModel? selectedModel = null;
        foreach (CommandModel model in models)
        {
            if (model.Root.MatchesName(route.CommandPath[0]))
            {
                if (selectedModel is not null)
                {
                    throw new ArgumentException("A multicall command path must identify exactly one model root.", nameof(route));
                }

                selectedModel = model;
            }
        }

        if (selectedModel is null)
        {
            throw new ArgumentException("The multicall command path does not identify a supplied model root.", nameof(route));
        }

        List<CommandDefinition> path = [selectedModel.Root];
        for (int index = 1; index < route.CommandPath.Count; index++)
        {
            CommandDefinition? next = null;
            foreach (CommandDefinition child in path[^1].Children)
            {
                if (child.MatchesName(route.CommandPath[index]))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                throw new ArgumentException("The multicall command path does not identify a command in its model.", nameof(route));
            }

            path.Add(next);
        }

        return new MulticallTarget(selectedModel, path);
    }

    private sealed record MulticallTarget(CommandModel Model, IReadOnlyList<CommandDefinition> Path);

    private async ValueTask<HostInvocationResult> InvokeForHostAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CliHostOptions hostOptions,
        CancellationToken cancellationToken,
        IReadOnlyList<CommandDefinition>? initialPath = null)
    {
        try
        {
            InvocationResult result = await InvokeCoreAsync(model, arguments, hostOptions, cancellationToken, initialPath).ConfigureAwait(false);
            return new HostInvocationResult(result, null, null);
        }
        catch (Exception exception)
        {
            CliExceptionResult? mapped = hostOptions.ExceptionMappings.Resolve(exception)
                ?? (exception as CliExitCodeException)?.Result;
            if (mapped is null)
            {
                throw;
            }

            return new HostInvocationResult(null, exception, mapped);
        }
    }

    private static int WriteHostResult(CommandModel model, HostInvocationResult result, CliHostOptions options, int initialPathLength = 1)
    {
        if (result.Invocation is not null)
        {
            return WriteHostResult(model, result.Invocation, options, initialPathLength);
        }

        CliExceptionResult failure = result.Failure
            ?? throw new InvalidOperationException("The host failure has no exception mapping.");
        CliExceptionRenderingContext context = new(
            result.Exception ?? throw new InvalidOperationException("The host failure has no original exception."),
            failure,
            options.ExecutableName);
        string rendered = options.ExceptionRenderer(context)
            ?? throw new InvalidOperationException("The exception renderer returned null.");
        if (rendered.Length > 0)
        {
            options.StandardError.WriteLine(rendered);
        }

        return failure.ExitCode;
    }

    private sealed record HostInvocationResult(InvocationResult? Invocation, Exception? Exception, CliExceptionResult? Failure);

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

    /// <summary>Parses and invokes one command with explicit host configuration.</summary>
    /// <param name="model">The validated command model.</param>
    /// <param name="arguments">Tokens excluding the executable name.</param>
    /// <param name="options">The host output, converter, and environment-binding settings.</param>
    /// <param name="cancellationToken">The caller-owned cancellation token.</param>
    /// <returns>The invocation outcome and any completed parse result.</returns>
    public ValueTask<InvocationResult> InvokeAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CliHostOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return InvokeCoreAsync(model, arguments, options, cancellationToken);
    }

    private async ValueTask<InvocationResult> InvokeCoreAsync(
        CommandModel model,
        IReadOnlyList<string> arguments,
        CliHostOptions hostOptions,
        CancellationToken cancellationToken,
        IReadOnlyList<CommandDefinition>? initialPath = null)
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
            parseResult = _parser.ParseForInvocation(model, arguments, cancellationToken, hostOptions, initialPath);
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
        CliHostOptions options,
        int initialPathLength = 1)
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
                    HelpDocument.CreateForInvocation(model, selectedCommand, options, initialPathLength),
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
