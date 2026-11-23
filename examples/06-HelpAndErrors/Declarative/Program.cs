using System;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace HelpAndErrors.Declarative;

[CliCommand("inspect", IsRoot = true, Description = "Inspect a numbered item.")]
internal sealed partial class InspectCommand
{
    [CliOption("count", IsRequired = true, Description = "Item number.")]
    public int Count { get; init; }

    [CliHandler]
    internal int Run()
    {
        if (Count < 0)
        {
            throw new CliExitCodeException(65, "The item number must not be negative.");
        }

        if (Count == 0)
        {
            throw new FileNotFoundException("Internal lookup details, not safe for public output.");
        }

        Console.WriteLine($"Item: {Count}");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        CliHostOptions options = new CliHostOptionsBuilder()
            .MapException<IOException>(74, "The requested item is unavailable.")
            .WithExceptionRenderer(static context => "inspect: " + context.Result.SafeMessage)
            .Build();
        return CliInvoker.RunAsync<InspectCommand>(args, options);
    }
}
