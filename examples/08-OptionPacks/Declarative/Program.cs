using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace OptionPacks.Declarative;

[CliCommand("08-option-packs", IsRoot = true, Description = "Demonstrate reusable nested option packs.")]
[CliChild<FetchCommand>]
[CliChild<InspectCommand>]
internal sealed partial class OptionPacksCommand
{
}

[CliCommand(Description = "Fetch a resource.")]
[CliPack<NetworkPack>("network", Title = "Network")]
internal sealed partial class FetchCommand
{
    [CliHandler]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Generated command handlers are instance methods.")]
    private int Run(CommandInvocationContext context)
    {
        OptionPackContext network = context.GetOptionPack("network");
        OptionPackContext retry = network.GetNestedPack("retry");
        if (!network.TryGetOptionValue(NetworkPack.Option0, out string? endpoint)
            || !retry.TryGetOptionValue(RetryPack.Option0, out int retries))
        {
            throw new InvalidOperationException("The generated network pack values are unavailable.");
        }

        Console.WriteLine($"Fetch {endpoint} with {retries} retries.");
        return 0;
    }

}

[CliCommand(Description = "Inspect a resource.")]
[CliPack<NetworkPack>("network", Title = "Network")]
internal sealed partial class InspectCommand
{
    [CliHandler]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Generated command handlers are instance methods.")]
    private int Run(CommandInvocationContext context)
    {
        OptionPackContext network = context.GetOptionPack("network");
        OptionPackContext retry = network.GetNestedPack("retry");
        if (!network.TryGetOptionValue(NetworkPack.Option0, out string? endpoint)
            || !retry.TryGetOptionValue(RetryPack.Option0, out int retries))
        {
            throw new InvalidOperationException("The generated network pack values are unavailable.");
        }

        Console.WriteLine($"Inspect {endpoint} with {retries} retries.");
        return 0;
    }

}

[CliOptionPack]
[CliPack<RetryPack>("retry", Title = "Retry")]
[CliPackOption<string>("--endpoint", IsRequired = true, Description = "Resource address.")]
internal sealed partial class NetworkPack
{
}

[CliOptionPack]
[CliPackOption<int>("--retries", IsRequired = true, Description = "Maximum retry count.")]
internal sealed partial class RetryPack
{
}

internal static class Program
{
    private static Task<int> Main(string[] args)
        => CliInvoker.RunAsync<OptionPacksCommand>(args);
}
