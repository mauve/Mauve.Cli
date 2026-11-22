using System;
using System.Collections.Generic;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ValueShapes.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        OptionDefinition tags = new(
            "--tag",
            typeof(IReadOnlyList<string>),
            OptionArity.RepeatedSingleCollection,
            elementType: typeof(string),
            isRequired: true,
            description: "A repeatable tag.");
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Show the verbose mode.");
        OptionDefinition filter = new(
            "--filter",
            typeof(string),
            OptionArity.OptionalScalar,
            allowsNull: true,
            bareValuePolicy: BareValuePolicy.Null,
            description: "An optional filter; bare means no filter.");
        PositionalDefinition paths = new(
            "paths",
            typeof(IReadOnlyList<string>),
            minimumValues: 0,
            maximumValues: null,
            elementType: typeof(string),
            description: "Remaining paths.");
        CommandModel model = CommandModel.Create(new CommandBuilder("shapes")
            .AddChild(new CommandBuilder("inspect")
                .AddOption(tags)
                .AddOption(verbose)
                .AddOption(filter)
                .AddPositional(paths)
                .SetHandler((context, _) =>
                {
                    IReadOnlyList<string> parsedTags = context.TryGetOptionValue(tags, out IReadOnlyList<string>? tagValues)
                        ? tagValues!
                        : throw new InvalidOperationException("The tag option is unavailable.");
                    bool parsedVerbose = context.TryGetOptionValue(verbose, out bool verboseValue)
                        ? verboseValue
                        : throw new InvalidOperationException("The verbose option is unavailable.");
                    IReadOnlyList<string> parsedPaths = context.TryGetPositionalValue(paths, out IReadOnlyList<string>? pathValues)
                        ? pathValues!
                        : throw new InvalidOperationException("The paths argument is unavailable.");
                    string? parsedFilter = context.TryGetOptionValue(filter, out string? filterValue) ? filterValue : null;
                    Console.WriteLine(
                        $"verbose={parsedVerbose}; filter={parsedFilter ?? "(null)"}; tags=[{string.Join(", ", parsedTags)}]; paths=[{string.Join(", ", parsedPaths)}]");
                    return 0;
                })),
            includeStandardHelp: true);
        return CliInvoker.Run(model, args);
    }

}
