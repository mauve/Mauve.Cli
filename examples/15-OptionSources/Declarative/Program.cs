using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace OptionSources.Declarative;

[CliCommand("sources", IsRoot = true, Description = "Print the source of each option.")]
internal sealed partial class SourcesCommand
{
    [CliOption(EnvironmentVariableBinding = EnvironmentVariableBinding.Disabled)]
    public string Mode { get; init; } = "local";

    [CliOption(EnvironmentVariableName = "SOURCE_REGION")]
    public string? Region { get; init; }

    [CliOption(IsRequired = true, EnvironmentVariableBinding = EnvironmentVariableBinding.Disabled)]
    public required string Profile { get; init; }

    [CliHandler]
    internal int Run(CommandInvocationContext context)
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
    }
}

internal static class Program
{
    private static int Main(string[] args)
        => CliInvoker.Run<SourcesCommand>(args, new CliHostOptions(enableEnvironmentVariables: true));
}
