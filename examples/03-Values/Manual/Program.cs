using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Values.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        OptionDefinition count = new(
            "count",
            typeof(int),
            OptionArity.RequiredScalar,
            shortName: 'c',
            isRequired: true,
            description: "A whole-number count.");
        OptionDefinition enabled = new(
            "--enabled",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Mark the value as enabled.");
        PositionalDefinition name = new("name", typeof(string), description: "Name to display.");
        CommandModel model = CommandModel.Create(new CommandBuilder("values")
            .AddChild(new CommandBuilder("show")
                .AddOption(count)
                .AddOption(enabled)
                .AddPositional(name)
                .SetHandler((context, _) =>
                {
                    int parsedCount = context.TryGetOptionValue(count, out int countValue)
                        ? countValue
                        : throw new InvalidOperationException("The count option is unavailable.");
                    bool parsedEnabled = context.TryGetOptionValue(enabled, out bool enabledValue)
                        ? enabledValue
                        : throw new InvalidOperationException("The enabled option is unavailable.");
                    string? parsedName = context.TryGetPositionalValue(name, out string? nameValue)
                        ? nameValue
                        : throw new InvalidOperationException("The name argument is unavailable.");
                    Console.WriteLine($"{parsedName}: count={parsedCount}; enabled={parsedEnabled}");
                    return 0;
                })),
            includeStandardHelp: true);
        return CliInvoker.Run(model, args);
    }

}
