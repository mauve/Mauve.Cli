using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Validation.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        OptionDefinition limit = new(
            "--limit",
            typeof(int),
            OptionArity.RequiredScalar,
            description: "Limit from 1 through 50.",
            defaultValuePolicy: DefaultValuePolicy.Constant,
            defaultValue: 10,
            defaultDisplay: "10",
            validators:
            [
                new ValueValidator<int>(static (value, _) => value is >= 1 and <= 50
                    ? ValueValidationOutcome.Success
                    : ValueValidationOutcome.Failure("range.invalid"))
            ]);
        CommandModel model = CommandModel.Create(new CommandBuilder("validate")
            .AddOption(limit)
            .SetHandler((context, _) =>
            {
                if (!context.TryGetOptionValue(limit, out int value))
                {
                    return 2;
                }

                Console.WriteLine($"Limit: {value}");
                return 0;
            }), includeStandardHelp: true);
        return CliInvoker.RunAsync(model, args);
    }
}
