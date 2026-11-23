using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace OptionSources.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        OptionDefinition mode = new(
            "--mode",
            typeof(string),
            OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant,
            defaultValue: "local",
            environmentVariableBinding: EnvironmentVariableBinding.Disabled);
        OptionDefinition region = new(
            "--region",
            typeof(string),
            OptionArity.RequiredScalar,
            environmentVariableName: "SOURCE_REGION");
        OptionDefinition profile = new(
            "--profile",
            typeof(string),
            OptionArity.RequiredScalar,
            isRequired: true,
            environmentVariableBinding: EnvironmentVariableBinding.Disabled);

        CommandModel model = CommandModel.Create(new CommandBuilder("sources")
            .AddOption(mode)
            .AddOption(region)
            .AddOption(profile)
            .SetHandler((context, _) =>
            {
                foreach (ParsedValue parsedValue in context.ParseResult.Values)
                {
                    if (parsedValue.Option is OptionDefinition option
                        && option.Name is not ("--help" or "--help-tree"))
                    {
                        Console.WriteLine($"{option.Name}: {parsedValue.Origin}");
                    }
                }

                return 0;
            }), includeStandardHelp: true);

        CliHostOptions hostOptions = new(enableEnvironmentVariables: true);
        return CliInvoker.Run(model, args, hostOptions);
    }
}
