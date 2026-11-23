using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace HelpAndErrors.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        OptionDefinition count = new(
            "--count",
            typeof(int),
            OptionArity.RequiredScalar,
            isRequired: true,
            description: "Item number.");
        OptionDefinition mode = new(
            "--mode",
            typeof(string),
            OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant,
            defaultValue: "safe",
            defaultDisplay: "safe",
            description: "Inspection mode.",
            validators: [CliValidators.AllowedValues("safe", "fast")]);
        OptionDefinition tags = new(
            "--tag",
            typeof(IReadOnlyList<string>),
            OptionArity.RepeatedSingleCollection,
            elementType: typeof(string),
            description: "Tag the item.");
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited,
            description: "Show detailed output.");
        OptionPackBuilder inspection = new OptionPackBuilder().AddOption(mode);
        DirectiveBuilder notice = new("notice")
        {
            Description = "Print a notice before command execution."
        };
        _ = notice.SetHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine("Notice directive ran.");
        });
        CommandBuilder command = new("inspect")
        {
            Description = "Inspect a numbered item.",
            LongDescription = "Use this command to inspect a numbered item and explore the help output. "
                + "Choose a safe or fast inspection mode, add tags to describe the item, and "
                + "use verbose output when you need more detail. The show and summary "
                + "subcommands demonstrate inherited options. The notice directive runs "
                + "before command processing begins."
        };
        CommandModel model = CommandModel.Create(command
            .AddExample("$0 --count=4 --mode=safe")
            .AddExample("$0 '[notice]' --count=4 --mode=fast")
            .AddDirective(notice)
            .AddOption(count)
            .AddOption(tags)
            .AddOption(verbose)
            .AddPack("inspection", inspection, "Inspection", "Inspection settings.")
            .AddChild(new CommandBuilder("show") { Description = "Show an item." }
                .SetHandler((context, _) => WriteChild(context, verbose, "Show")))
            .AddChild(new CommandBuilder("summary") { Description = "Summarize items." }
                .SetHandler((context, _) => WriteChild(context, verbose, "Summary")))
            .SetHandler((context, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!context.TryGetOptionValue(count, out int value)
                    || !context.GetOptionPack("inspection").TryGetOptionValue(mode, out string? modeValue)
                    || !context.TryGetOptionValue(verbose, out bool verboseValue))
                {
                    throw new InvalidOperationException("Validated input has no item number or mode.");
                }

                if (value < 0)
                {
                    throw new CliExitCodeException(65, "The item number must not be negative.");
                }

                if (value == 0)
                {
                    throw new FileNotFoundException("Internal lookup details, not safe for public output.");
                }

                _ = context.TryGetOptionValue(tags, out IReadOnlyList<string>? tagValues);
                Console.WriteLine($"Item: {value}; mode: {modeValue}; tags: {string.Join(", ", tagValues ?? [])}; verbose: {verboseValue}");
                return 0;
            }), includeStandardHelp: true);
        ExceptionMappingRegistry mappings = new ExceptionMappingRegistryBuilder()
            .Map<IOException>(74, "The requested item is unavailable.")
            .Build();
        CliHostOptions options = new(
            exceptionMappings: mappings,
            exceptionRenderer: static context => "inspect: " + context.Result.SafeMessage);
        return CliInvoker.RunAsync(model, args, options);
    }

    private static int WriteChild(CommandInvocationContext context, OptionDefinition verbose, string label)
    {
        if (!context.TryGetOptionValue(verbose, out bool value))
        {
            throw new InvalidOperationException("Validated input has no verbosity setting.");
        }

        Console.WriteLine($"{label}; verbose: {value}");
        return 0;
    }
}
