using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace OptionScope.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        OptionDefinition environment = new(
            "--environment",
            typeof(string),
            OptionArity.RequiredScalar,
            isRequired: true,
            description: "Deployment environment.",
            scope: Mauve.Cli.Definitions.OptionScope.Inherited);
        OptionDefinition rootOnly = new(
            "--root-only",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Available only on the root.");
        OptionDefinition brief = new(
            "--brief",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "Use brief output.");
        CommandBuilder root = new("deploy")
        {
            Description = "Show deployment settings."
        };
        CommandBuilder show = new("show")
        {
            Description = "Show the selected environment."
        };
        _ = show.AddOption(brief);
        _ = show.SetHandler((context, _) =>
        {
            bool hasEnvironment = context.ParseResult.TryGetOptionValue(
                context.ParseResult.SelectedPath[0],
                environment,
                out string? selectedEnvironment);
            bool hasBrief = context.TryGetOptionValue(brief, out bool briefOutput);
            if (!hasEnvironment || !hasBrief)
            {
                return 2;
            }

            Console.WriteLine($"Environment: {selectedEnvironment}; brief={briefOutput}");
            return 0;
        });
        _ = root.AddOption(environment);
        _ = root.AddOption(rootOnly);
        _ = root.AddChild(show);
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        return CliInvoker.RunAsync(model, args);
    }
}
