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
    [CliOption("--tag", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true, Description = "A repeatable tag.")]
    public partial IReadOnlyList<string> Tags { get; }

    [CliOption("--verbose", Description = "Show the verbose mode.")]
    public partial bool Verbose { get; }

    [CliOption(
        "--filter",
        Arity = OptionArity.OptionalScalar,
        AllowsNull = true,
        BareValuePolicy = BareValuePolicy.Null,
        Description = "An optional filter; bare means no filter.")]
    public partial string? Filter { get; }

    [CliArgument("paths", MinimumValues = 0, MaximumValues = -1, Description = "Remaining paths.")]
    public partial IReadOnlyList<string> Paths { get; }

    [CliHandler]
    private int Run()
    {
        // The Filter property throws when --filter is absent; TryGetFilter distinguishes
        // "absent" from an explicit bare --filter (which binds null).
        string? filter = TryGetFilter(out string? value) ? value : null;
        Console.WriteLine(
            $"verbose={Verbose}; filter={filter ?? "(null)"}; tags=[{string.Join(", ", Tags)}]; paths=[{string.Join(", ", Paths)}]");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ShapesCommand>(args);
}
