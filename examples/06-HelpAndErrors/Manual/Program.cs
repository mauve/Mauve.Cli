using System;
using System.IO;
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
                    throw new InvalidOperationException("Validated input has no item number.");
                }

                if (value < 0)
                {
                    throw new CliExitCodeException(65, "The item number must not be negative.");
                }

                if (value == 0)
                {
                    throw new FileNotFoundException("Internal lookup details, not safe for public output.");
                }

                Console.WriteLine($"Item: {value}");
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
}
