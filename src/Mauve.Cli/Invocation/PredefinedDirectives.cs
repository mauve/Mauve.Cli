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

    /// <summary>Creates a suggest directive using the invocation's configured host writers.</summary>
    /// <param name="service">An optional configured completion service.</param>
    /// <param name="providers">Optional registered completion providers.</param>
    /// <returns>A new suggest directive builder.</returns>
    public static DirectiveBuilder CreateSuggest(
        CompletionService? service = null,
        CompletionProviderRegistry? providers = null)
        => CreateSuggestCore(null, null, service, providers);

    /// <summary>Creates a directive that writes dotnet-suggest completion candidates.</summary>
    /// <param name="output">The output for newline-delimited insertion texts.</param>
    /// <param name="errors">The output for completion failures.</param>
    /// <param name="service">An optional configured completion service.</param>
    /// <param name="providers">Optional registered completion providers.</param>
    /// <returns>A new suggest directive builder.</returns>
    public static DirectiveBuilder CreateSuggest(
        TextWriter output,
        TextWriter errors,
        CompletionService? service = null,
        CompletionProviderRegistry? providers = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);
        return CreateSuggestCore(output, errors, service, providers);
    }

    private static DirectiveBuilder CreateSuggestCore(
        TextWriter? output,
        TextWriter? errors,
        CompletionService? service,
        CompletionProviderRegistry? providers)
    {
        DirectiveValueDefinition position = new("position", typeof(int));
        CompletionService completion = service ?? new CompletionService();
        return new DirectiveBuilder("suggest")
        {
            Description = "Produce command-line completion suggestions."
        }.AddValue(position).SetValueTaskHandler(async (context, cancellationToken) =>
        {
            TextWriter standardOutput = output ?? context.HostOptions.StandardOutput;
            TextWriter standardError = errors ?? context.HostOptions.StandardError;
            if (!context.TryGetValue(position, out int cursor))
            {
                throw new InvalidOperationException("The suggest position is unavailable.");
            }

            HashSet<int> directiveIndices = [];
            foreach (Parsing.ParsedDirective directive in context.ParseResult.Directives)
            {
                directiveIndices.UnionWith(directive.TokenIndices);
            }

            string? payload = null;
            for (int index = context.ParseResult.Arguments.Count - 1; index >= 0; index--)
            {
                if (!directiveIndices.Contains(index))
                {
                    payload = context.ParseResult.Arguments[index];
                    break;
                }
            }

            if (payload is null)
            {
                await standardError.WriteLineAsync("Completion requires a command-line payload.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            CompletionRequest request;
            try
            {
                request = SuggestCommandLine.CreateRequest(context.Model, payload, cursor);
            }
            catch (ArgumentException)
            {
                await standardError.WriteLineAsync("Invalid completion payload or cursor position.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            CompletionResult result = await completion.CompleteAsync(request, providers, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Outcome != CompletionOutcome.Completed)
            {
                await standardError.WriteLineAsync($"Completion failed: {result.Outcome}.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            if (result.Candidates.Any(static candidate => candidate.InsertionText.Contains('\n', StringComparison.Ordinal)
                || candidate.InsertionText.Contains('\r', StringComparison.Ordinal)))
            {
                await standardError.WriteLineAsync("Completion returned a multiline candidate.".AsMemory(), cancellationToken).ConfigureAwait(false);
                return DirectiveResult.StopProcessing(2);
            }

            foreach (CompletionCandidate candidate in result.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await standardOutput.WriteLineAsync(candidate.InsertionText.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            return DirectiveResult.StopProcessing(0);
        });
    }
}
