using System;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace ExceptionHandling.Declarative;

[CliCommand("exceptions", IsRoot = true, Description = "Handle expected command failures.")]
internal sealed partial class ExceptionCommand
{
    [CliOption(Description = "success, direct or missing.")]
    public string Scenario { get; init; } = "success";

    [CliHandler]
    internal int Run()
    {
        switch (Scenario)
        {
            case "success":
                Console.WriteLine("Completed.");
                return 0;
            case "direct":
                throw new CliExitCodeException(65, "The requested operation cannot be completed.");
            case "missing":
                throw new FileNotFoundException("Private path details.");
            default:
                throw new CliExitCodeException(64, "Unknown scenario; choose success, direct or missing.");
        }
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        CliHostOptions options = new CliHostOptionsBuilder()
            .MapException<IOException>(74, "The requested file is unavailable.")
            .WithExceptionRenderer(static context => "Error: " + context.Result.SafeMessage)
            .Build();

        return CliInvoker.RunAsync<ExceptionCommand>(args, options);
    }
}
