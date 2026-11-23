using System;
using System.ComponentModel.DataAnnotations;
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
    [Required]
    [CliOption("count", ShortName = 'c', Description = "A whole-number count.")]
    public int Count { get; init; }

    [CliOption("enabled", Description = "Mark the value as enabled.")]
    public bool Enabled { get; init; }

    [CliArgument(Description = "Name to display.")]
    public required string Name { get; init; }

    [CliHandler]
    internal int Run()
    {
        Console.WriteLine($"{Name}: count={Count}; enabled={Enabled}");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ValuesCommand>(args);
}
