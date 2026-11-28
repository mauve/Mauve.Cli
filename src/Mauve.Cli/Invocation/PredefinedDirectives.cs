using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;

namespace Mauve.Cli.Invocation;

/// <summary>Creates opt-in root directives for debugging and command-line completion.</summary>
public static class PredefinedDirectives
{
    /// <summary>Creates a shell-completion directive for a command on the user's PATH.</summary>
    /// <param name="commandName">The command name to register in the shell.</param>
    /// <param name="service">An optional configured completion service.</param>
    /// <param name="providers">Optional registered completion providers.</param>
    /// <returns>A new completion directive builder.</returns>
    public static DirectiveBuilder CreateCompletion(
        string commandName,
        CompletionService? service = null,
        CompletionProviderRegistry? providers = null)
    {
        ShellCompletion.ValidateCommand(commandName);
        DirectiveValueDefinition mode = new("mode", typeof(string));
        return new DirectiveBuilder("completion")
        {
            Description = "Generate shell completion or handle a completion request.",
            IsCompletion = true
        }.AddValue(mode).SetValueTaskHandler(async (context, cancellationToken) =>
        {
            if (!context.TryGetValue(mode, out string? selectedMode) || selectedMode is null)
            {
                throw new InvalidOperationException("The completion mode is unavailable.");
            }

            TextWriter output = context.HostOptions.StandardOutput;
            TextWriter errors = context.HostOptions.StandardError;
            if (selectedMode is "bash" or "zsh" or "fish" or "powershell")
            {
                await output.WriteAsync(ShellCompletion.CreateScript(commandName, selectedMode).AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(0);
            }

            if (selectedMode != "query-v1")
            {
                await errors.WriteLineAsync("Unsupported completion mode.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            IReadOnlyList<string> arguments = context.ParseResult.Arguments;
            int directiveEnd = context.Directive.TokenIndices[^1];
            if (directiveEnd + 4 >= arguments.Count || arguments[directiveEnd + 1] != "--"
                || directiveEnd + 5 != arguments.Count
                || arguments[directiveEnd + 2] is not ("bash" or "bash-full" or "zsh" or "fish" or "powershell"))
            {
                await errors.WriteLineAsync("Invalid completion request.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            CompletionRequest request;
            try
            {
                request = ShellCompletion.CreateRequest(
                    context.Model, arguments[directiveEnd + 3], arguments[directiveEnd + 4], arguments[directiveEnd + 2]);
            }
            catch (ArgumentException)
            {
                await errors.WriteLineAsync("Invalid completion payload or cursor position.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            CompletionService completion = service ?? new CompletionService(
                context.HostOptions.Converters,
                context.HostOptions.NumericCulture,
                context.HostOptions.Syntax);
            CompletionResult result = await completion.CompleteAsync(request, providers, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Outcome != CompletionOutcome.Completed || !result.IsComplete)
            {
                await errors.WriteLineAsync($"Completion failed: {result.Outcome}.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            string[] encoded;
            try
            {
                bool fullToken = arguments[directiveEnd + 2] != "bash";
                encoded = [.. result.Candidates.Select(candidate => ShellCompletion.EncodeCandidate(candidate, request, fullToken))];
            }
            catch (ArgumentException)
            {
                await errors.WriteLineAsync("Completion returned an unsupported candidate.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            foreach (string candidate in encoded)
            {
                await output.WriteLineAsync(candidate.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return DirectiveResult.StopProcessing(0);
        });
    }

    /// <summary>Creates a directive that waits for a debugger, then continues.</summary>
    /// <returns>A new debug directive builder.</returns>
    public static DirectiveBuilder CreateDebug()
        => CreateDebug(static () => Debugger.IsAttached, static token => Task.Delay(100, token));

    internal static DirectiveBuilder CreateDebug(Func<bool> isAttached, Func<CancellationToken, Task> delay)
    {
        return new DirectiveBuilder("debug")
        {
            Description = "Wait for a debugger to attach."
        }.SetTaskHandler(async (_, cancellationToken) =>
        {
            while (!isAttached())
            {
                await delay(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        });
    }

}
