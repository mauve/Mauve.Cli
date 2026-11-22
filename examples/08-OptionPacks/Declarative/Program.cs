using System;
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
internal sealed partial class FetchCommand(NetworkPack network)
{
    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"Fetch {network.Endpoint} with {network.Retry.Retries} retries.");
        return 0;
    }

}

[CliCommand(Description = "Inspect a resource.")]
internal sealed partial class InspectCommand(NetworkPack network)
{
    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"Inspect {network.Endpoint} with {network.Retry.Retries} retries.");
        return 0;
    }

}

[CliOptionPack(Title = "Network", Description = "Network options.")]
internal sealed partial class NetworkPack(RetryPack retry)
{
    public RetryPack Retry { get; } = retry;

    [CliOption("--endpoint", IsRequired = true, Description = "Resource address.")]
    public partial string Endpoint { get; }
}

[CliOptionPack(Title = "Retry", Description = "Retry options.")]
internal sealed partial class RetryPack
{
    [CliOption("--retries", IsRequired = true, Description = "Maximum retry count.")]
    public partial int Retries { get; }
}

internal static class Program
{
    private static Task<int> Main(string[] args)
        => CliInvoker.RunAsync<OptionPacksCommand>(args);
}
