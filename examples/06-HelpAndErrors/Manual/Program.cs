using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

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
        CommandBuilder command = new("inspect")
        {
            Description = "Inspect a numbered item."
        };
        CommandModel model = CommandModel.Create(command
            .AddOption(count)
            .SetHandler((context, _) =>
            {
                if (!context.TryGetOptionValue(count, out int value))
                {
                    return 2;
                }

                Console.WriteLine($"Item: {value}");
                return 0;
            }), includeStandardHelp: true);
        return CliInvoker.RunAsync(model, args);
    }
}
