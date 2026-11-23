using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Validation.Declarative;

[CliCommand("validate", IsRoot = true, Description = "Validate a numeric limit.")]
internal sealed partial class ValidateCommand
{
    [CliOption(
        "--limit",
        Arity = OptionArity.RequiredScalar,
        Description = "Limit from 1 through 50.")]
    public int Limit { get; init; } = 10;

    [CliHandler]
    internal int Run()
    {
        int limit = Limit;
        if (limit is < 1 or > 50)
        {
            Console.Error.WriteLine("Limit must be between 1 and 50.");
            return 2;
        }

        Console.WriteLine($"Limit: {limit}");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args) => CliInvoker.RunAsync<ValidateCommand>(args);
}
