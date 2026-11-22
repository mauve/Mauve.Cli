using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace OptionScope.Declarative;

[CliCommand("deploy", IsRoot = true, Description = "Show deployment settings.")]
[CliChild<ShowCommand>]
[CliPack<GlobalOptions>("global", Title = "Global")]
internal sealed partial class DeployCommand
{
    [CliOption("--root-only", Arity = OptionArity.BooleanFlag, Description = "Available only on the root.")]
    public partial bool RootOnly { get; }
}

[CliOptionPack]
[CliPackOption<string>(
    "--environment",
    IsRequired = true,
    Scope = Mauve.Cli.Definitions.OptionScope.Inherited,
    Description = "Deployment environment.")]
internal sealed partial class GlobalOptions
{
}

[CliCommand("show", Description = "Show the selected environment.")]
internal sealed partial class ShowCommand
{
    [CliOption("--brief", Arity = OptionArity.BooleanFlag, Description = "Use brief output.")]
    public partial bool Brief { get; }

    [CliHandler]
    internal int Run(CommandInvocationContext context)
    {
        OptionPackContext global = context.GetOptionPack(context.ParseResult.SelectedPath[0], "global");
        if (!global.TryGetOptionValue(GlobalOptions.Option0, out string? environment))
        {
            return 2;
        }

        Console.WriteLine($"Environment: {environment}; brief={Brief}");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args) => CliInvoker.RunAsync<DeployCommand>(args);
}
