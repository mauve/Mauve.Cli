using System;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace OptionScope.Declarative;

[CliCommand("deploy", IsRoot = true, Description = "Show deployment settings.")]
[CliChild<ShowCommand>]
internal sealed partial class DeployCommand(GlobalOptions global)
{
    public GlobalOptions Global { get; } = global;

    [CliOption("root-only", Arity = OptionArity.BooleanFlag, Description = "Available only on the root.")]
    public partial bool RootOnly { get; }
}

[CliOptionPack(Title = "Global", Description = "Global deployment settings.")]
internal sealed partial class GlobalOptions
{
    [CliOption(
        "--environment",
        IsRequired = true,
        Scope = Mauve.Cli.Definitions.OptionScope.Inherited,
        Description = "Deployment environment.")]
    public partial string Environment { get; }
}

[CliCommand("show", Description = "Show the selected environment.")]
internal sealed partial class ShowCommand
{
    [CliOption("brief", Arity = OptionArity.BooleanFlag, Description = "Use brief output.")]
    public partial bool Brief { get; }

    [CliHandler]
    internal int Run(CommandInvocationContext context)
    {
        OptionDefinition environmentOption = context.ParseResult.SelectedPath[0].Options
            .Single(static option => option.Name == "--environment");
        if (!context.TryGetOptionValue(environmentOption, out string? environment))
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
