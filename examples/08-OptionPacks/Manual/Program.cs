using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace OptionPacks.Manual;

internal static class Program
{
    private static readonly OptionDefinition _endpoint = new(
        "--endpoint",
        typeof(string),
        OptionArity.RequiredScalar,
        isRequired: true,
        description: "Resource address.");
    private static readonly OptionDefinition _retries = new(
        "--retries",
        typeof(int),
        OptionArity.RequiredScalar,
        isRequired: true,
        description: "Maximum retry count.");
    private static readonly OptionPackBuilder _retryPack = new OptionPackBuilder()
        .AddOption(_retries);
    private static readonly OptionPackBuilder _networkPack = new OptionPackBuilder()
        .AddOption(_endpoint)
        .AddPack("retry", _retryPack, "Retry");

    private static Task<int> Main(string[] args)
        => CliInvoker.RunAsync(CreateModel(), args);

    private static CommandModel CreateModel()
        => CommandModel.Create(
            new CommandBuilder("08-option-packs")
            {
                Description = "Demonstrate reusable nested option packs."
            }
                .AddChild(new CommandBuilder("fetch")
                {
                    Description = "Fetch a resource."
                }
                    .AddPack("network", _networkPack, "Network")
                    .SetHandler((context, _) => Run(context, "Fetch")))
                .AddChild(new CommandBuilder("inspect")
                {
                    Description = "Inspect a resource."
                }
                    .AddPack("network", _networkPack, "Network")
                    .SetHandler((context, _) => Run(context, "Inspect"))),
            includeStandardHelp: true);

    private static int Run(CommandInvocationContext context, string action)
    {
        OptionPackContext network = context.GetOptionPack("network");
        OptionPackContext retry = network.GetNestedPack("retry");
        if (!network.TryGetOptionValue(_endpoint, out string? endpoint)
            || !retry.TryGetOptionValue(_retries, out int retries))
        {
            throw new InvalidOperationException("The explicit network pack values are unavailable.");
        }

        Console.WriteLine($"{action} {endpoint} with {retries} retries.");
        return 0;
    }

}
