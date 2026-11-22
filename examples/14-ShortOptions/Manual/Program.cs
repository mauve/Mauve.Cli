using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ShortOptions.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        OptionDefinition verbosity = new(
            "verbose",
            typeof(int),
            OptionArity.Counting,
            shortName: 'v',
            description: "Increase verbosity.");
        OptionDefinition all = new(
            "all",
            typeof(bool),
            OptionArity.BooleanFlag,
            shortName: 'a',
            description: "Include all items.");
        OptionDefinition force = new(
            "force",
            typeof(bool),
            OptionArity.BooleanFlag,
            shortName: 'f',
            description: "Apply changes without prompting.");
        CommandModel model = CommandModel.Create(new CommandBuilder("short-options")
            .AddOption(verbosity)
            .AddOption(all)
            .AddOption(force)
            .SetHandler((context, _) =>
            {
                int parsedVerbosity = context.TryGetOptionValue(verbosity, out int verbosityValue)
                    ? verbosityValue
                    : throw new InvalidOperationException("The verbosity option is unavailable.");
                bool parsedAll = context.TryGetOptionValue(all, out bool allValue)
                    ? allValue
                    : throw new InvalidOperationException("The all option is unavailable.");
                bool parsedForce = context.TryGetOptionValue(force, out bool forceValue)
                    ? forceValue
                    : throw new InvalidOperationException("The force option is unavailable.");
                Console.WriteLine($"verbosity={parsedVerbosity}; all={parsedAll}; force={parsedForce}");
                return 0;
            }),
            includeStandardHelp: true);

        return CliInvoker.Run(model, args);
    }
}
