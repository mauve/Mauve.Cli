using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Completion.Manual;

internal static class Program
{
    private static readonly OptionDefinition _color = new(
        "--color",
        typeof(string),
        OptionArity.RequiredScalar,
        isRequired: true,
        description: "Search result color.");
    private static readonly PositionalDefinition _query = new("query", typeof(string));
    private static readonly OptionPackBuilder _filterPack = new OptionPackBuilder()
        .AddOption(_color);
    private static readonly CompletionProviderRegistry _providers = new CompletionProviderRegistryBuilder()
        .Register(_color, GetColors)
        .Build();

    private static Task<int> Main(string[] args)
    {
        CommandModel model = CreateModel();
        return args is ["--demo-completion"]
            ? ShowCompletionAsync(model)
            : CliInvoker.RunAsync(model, args);
    }

    private static CommandModel CreateModel()
    {
        return CommandModel.Create(
            new CommandBuilder("09-completion")
                .AddDirective(PredefinedDirectives.CreateCompletion("09-completion", providers: _providers))
                .AddChild(new CommandBuilder("search")
                    .AddPack("filters", _filterPack, "Filters")
                    .AddPositional(_query)
                    .SetHandler((context, _) => RunSearch(context)
                )),
            includeStandardHelp: true);
    }

    private static int RunSearch(CommandInvocationContext context)
    {
        OptionPackContext filters = context.GetOptionPack("filters");
        if (!filters.TryGetOptionValue(_color, out string? color)
            || !context.TryGetPositionalValue(_query, out string? query))
        {
            throw new InvalidOperationException("The explicit search values are unavailable.");
        }

        Console.WriteLine($"Search {query} using {color}.");
        return 0;
    }

    private static async Task<int> ShowCompletionAsync(CommandModel model)
    {
        CompletionService service = new();
        await ShowCandidatesAsync(
            "Command",
            service,
            new CompletionRequest(model, ["se"], 0, 2)).ConfigureAwait(false);
        await ShowCandidatesAsync(
            "Option",
            service,
            new CompletionRequest(model, ["search", "--c"], 1, 3)).ConfigureAwait(false);
        string valueToken = "--color=bl";
        await ShowCandidatesAsync(
            "Value",
            service,
            new CompletionRequest(model, ["search", valueToken], 1, valueToken.Length)).ConfigureAwait(false);
        return 0;
    }

    private static async Task ShowCandidatesAsync(
        string label,
        CompletionService service,
        CompletionRequest request)
    {
        CompletionResult result = await service.CompleteAsync(request, _providers).ConfigureAwait(false);
        if (result.Outcome != CompletionOutcome.Completed || !result.IsComplete)
        {
            throw new InvalidOperationException($"{label} completion did not complete successfully.");
        }

        foreach (CompletionCandidate candidate in result.Candidates)
        {
            Console.WriteLine($"{label}: {candidate.InsertionText}");
        }
    }

    private static async IAsyncEnumerable<CompletionValue> GetColors(
        CompletionContext _,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new CompletionValue("blue", description: "Blue results.");
        yield return new CompletionValue("black", description: "Black results.");
        await Task.Yield();
    }

}
