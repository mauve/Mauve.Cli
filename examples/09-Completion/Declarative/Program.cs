using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Completion.Declarative;

[CliCommand("09-completion", IsRoot = true, Description = "Demonstrate logical-token completion.")]
[CliChild<SearchCommand>]
internal sealed partial class CompletionCommand
{
}

[CliCommand(Description = "Search without changing data.")]
[CliPack<ColorPack>("filters", Title = "Filters")]
internal sealed partial class SearchCommand
{
    [CliArgument(Description = "Search text.")]
    public partial string Query { get; }

    [CliHandler]
    private int Run(CommandInvocationContext context)
    {
        OptionPackContext filters = context.GetOptionPack("filters");
        if (!filters.TryGetOptionValue(ColorPack.Option0, out string? color))
        {
            throw new InvalidOperationException("The generated color option is unavailable.");
        }

        Console.WriteLine($"Search {Query} using {color}.");
        return 0;
    }

}

[CliOptionPack]
[CliPackOption<string>("--color", IsRequired = true, Description = "Search result color.")]
internal sealed partial class ColorPack
{
}

internal static class Program
{
    private static readonly CompletionProviderRegistry _providers = new CompletionProviderRegistryBuilder()
        .Register(ColorPack.Option0, GetColors)
        .Build();

    private static Task<int> Main(string[] args)
    {
        return args is ["--demo-completion"]
            ? ShowCompletionAsync(CompletionCommand.CreateModel(includeStandardHelp: true))
            : CliInvoker.RunAsync<CompletionCommand>(
                args,
                [PredefinedDirectives.CreateCompletion("09-completion", providers: _providers)]);
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
