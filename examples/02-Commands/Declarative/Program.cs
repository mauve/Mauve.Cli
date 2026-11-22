using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace Commands.Declarative;

[CliCommand("commands", IsRoot = true, Description = "Choose a greeting command.")]
[CliChild<GreetCommand>]
internal sealed partial class CommandsCommand
{
}

[CliCommand(Aliases = ["hi"], Description = "Greet someone.")]
internal sealed partial class GreetCommand
{
    [CliArgument(Description = "Person to greet.")]
    public partial string Name { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"Hello, {Name}!");
        return 0;
    }
}

[CliCommand(Description = "Say goodbye to someone.")]
[CliParent<CommandsCommand>]
internal sealed partial class GoodbyeCommand
{
    [CliArgument(Description = "Person to address.")]
    public partial string Name { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"Goodbye, {Name}!");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return CliInvoker.RunAsync<CommandsCommand>(
            args,
            [PredefinedDirectives.CreateDebug(), PredefinedDirectives.CreateCompletion("commands")]);
    }
}
