// User-facing example of building and invoking an explicit command model.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace FileTool.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CliHostOptions options = new();
        CommandModel model = CreateModel();
        if (args is ["--verify-aot"])
        {
            await VerifyAotAsync(model).ConfigureAwait(false);
            Write("AOT smoke check passed.");
            return 0;
        }

        return await CliInvoker.RunAsync(model, args, options).ConfigureAwait(false);
    }

    private static CommandModel CreateModel()
    {
        PositionalDefinition source = new("source", typeof(string));
        PositionalDefinition destination = new("destination", typeof(string));
        OptionDefinition overwrite = new(
            "--overwrite",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Replace an existing destination.");
        return CommandModel.Create(
            new CommandBuilder("file-tool")
                .AddDirective(PredefinedDirectives.CreateDebug())
                .AddDirective(PredefinedDirectives.CreateSuggest())
                .AddChild(new CommandBuilder("copy")
                    .AddOption(overwrite)
                    .AddPositional(source)
                    .AddPositional(destination)
                    .SetHandler((context, _) =>
                    {
                        if (!context.TryGetPositionalValue(source, out string? sourcePath)
                            || !context.TryGetPositionalValue(destination, out string? destinationPath)
                            || !context.TryGetOptionValue(overwrite, out bool replace))
                        {
                            return 2;
                        }

                        Write($"Copy {sourcePath} to {destinationPath}; overwrite={replace}");
                        return 0;
                    })),
            includeStandardHelp: true);
    }

    private static async Task VerifyAotAsync(CommandModel model)
    {
        CliInvoker invoker = new();
        InvocationResult valid = await invoker.InvokeAsync(
            model,
            ["copy", "--overwrite", "source.txt", "backup.txt"]).ConfigureAwait(false);
        if (valid.Outcome != InvocationOutcome.Completed
            || valid.ExitCode != 0
            || string.Join(" ", valid.ParseResult!.SelectedCommand!.Path) != "file-tool copy")
        {
            throw new InvalidOperationException("The explicit FileTool copy command did not run successfully.");
        }

        InvocationResult invalid = await invoker.InvokeAsync(model, ["copy", "source.txt"]).ConfigureAwait(false);
        if (invalid.Outcome != InvocationOutcome.InvalidInput)
        {
            throw new InvalidOperationException("The explicit FileTool copy command accepted missing input.");
        }
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static void Write(string message) => Console.WriteLine(message);
}
