using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Examples.Aot;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace FileTool.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--verify-aot"])
        {
            CommandModel aotModel = AotScenario.CreateExplicitModel(out ScenarioHandles handles);
            ConverterRegistry converters = AotScenario.CreateConverters();
            await AotScenario.VerifyAsync(aotModel, handles, converters).ConfigureAwait(false);
            Write("AOT scenarios passed.");
            return 0;
        }

        PositionalDefinition source = new("source", typeof(string));
        PositionalDefinition destination = new("destination", typeof(string));
        OptionDefinition overwrite = new(
            "--overwrite",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Replace an existing destination.");
        CommandModel model = CommandModel.Create(
            new CommandBuilder("file-tool")
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

        InvocationResult result = await new CliInvoker().InvokeAsync(model, args).ConfigureAwait(false);
        if (result.Outcome == InvocationOutcome.HelpRequested && result.ParseResult?.SelectedCommand is not null)
        {
            Write(HelpTextRenderer.Render(HelpDocument.Create(model, result.ParseResult.SelectedCommand)));
            return 0;
        }

        if (result.Outcome == InvocationOutcome.InvalidInput && result.ParseResult is not null)
        {
            foreach (ParseDiagnostic diagnostic in result.ParseResult.Diagnostics)
            {
                Write(diagnostic.Message);
            }

            return 2;
        }

        return result.ExitCode ?? 1;
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static void Write(string message)
    {
        Console.WriteLine(message);
    }
}
