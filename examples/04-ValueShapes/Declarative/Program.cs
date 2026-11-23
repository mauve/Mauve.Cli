using System;
using System.Collections.Generic;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ValueShapes.Declarative;

[CliCommand("shapes", IsRoot = true, Description = "Show different CLI value shapes.")]
[CliChild<InspectCommand>]
internal sealed partial class ShapesCommand
{
}

[CliCommand(Description = "Print a flag, repeated values, and remaining paths.")]
internal sealed partial class InspectCommand
{
    [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true, Description = "A repeatable tag.")]
    public required IReadOnlyList<string> Tags { get; init; }

    [CliOption("verbose", Description = "Show the verbose mode.")]
    public bool Verbose { get; init; }

    [CliOption(
        "--filter",
        Arity = OptionArity.OptionalScalar,
        AllowsNull = true,
        BareValuePolicy = BareValuePolicy.Null,
        Description = "An optional filter; bare means no filter.")]
    public string? Filter { get; init; }

    [CliArgument("paths", MinimumValues = 0, MaximumValues = -1, Description = "Remaining paths.")]
    public required IReadOnlyList<string> Paths { get; init; }

    [CliHandler]
    internal int Run()
    {
        Console.WriteLine(
            $"verbose={Verbose}; filter={Filter ?? "(null)"}; tags=[{string.Join(", ", Tags)}]; paths=[{string.Join(", ", Paths)}]");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ShapesCommand>(args);
}
