using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace HelpAndErrors.Declarative;

[CliCommand("inspect", IsRoot = true, Description = "Inspect a numbered item.")]
internal sealed partial class InspectCommand
{
    [CliOption("count", IsRequired = true, Description = "Item number.")]
    public partial int Count { get; }

    [CliHandler]
    internal int Run()
    {
        Console.WriteLine($"Item: {Count}");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args) => CliInvoker.RunAsync<InspectCommand>(args);
}
