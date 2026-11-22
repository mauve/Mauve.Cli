using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace Values.Declarative;

[CliCommand("values", IsRoot = true, Description = "Show typed option values.")]
[CliChild<ShowCommand>]
internal sealed partial class ValuesCommand
{
}

[CliCommand(Description = "Print a positional name, count, and optional flag.")]
internal sealed partial class ShowCommand
{
    [CliOption("--count", Aliases = ["-c"], IsRequired = true, Description = "A whole-number count.")]
    public partial int Count { get; }

    [CliOption("--enabled", Description = "Mark the value as enabled.")]
    public partial bool Enabled { get; }

    [CliArgument(Description = "Name to display.")]
    public partial string Name { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"{Name}: count={Count}; enabled={Enabled}");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ValuesCommand>(args);
}
