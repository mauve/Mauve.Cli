using System;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ExceptionHandling.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        OptionDefinition scenario = new("scenario", typeof(string), OptionArity.RequiredScalar,
            description: "success, direct or missing.",
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "success", defaultDisplay: "success");
        CommandBuilder command = new CommandBuilder("exceptions")
        {
            Description = "Handle expected command failures."
        }.AddOption(scenario).SetHandler((context, _) =>
        {
            if (!context.TryGetOptionValue(scenario, out string? selected))
            {
                throw new InvalidOperationException("The validated scenario is unavailable.");
            }

            switch (selected)
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
        });
        CommandModel model = CommandModel.Create(command, includeStandardHelp: true);
        CliHostOptions options = new CliHostOptionsBuilder()
            .MapException<IOException>(74, "The requested file is unavailable.")
            .WithExceptionRenderer(static context => "Error: " + context.Result.SafeMessage)
            .Build();

        return CliInvoker.RunAsync(model, args, options);
    }
}
