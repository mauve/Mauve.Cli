using System;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace Directives.Declarative;

[CliCommand("10-directives", IsRoot = true, Description = "Demonstrate custom command directives.")]
[CliChild<RunCommand>]
internal sealed partial class DirectivesCommand
{
    [CliDirective("notice", Description = "Print a notice before command execution.")]
    private int Notice(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("Notice directive ran.");
        return 0;
    }

}

[CliCommand(Description = "Run a no-op status command.")]
internal sealed partial class RunCommand
{
    [CliHandler]
    private int Run()
    {
        Console.WriteLine("Command ran.");
        return 0;
    }

}

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        return CliInvoker.RunAsync<DirectivesCommand>(
            args,
            [PredefinedDirectives.CreateCompletion("10-directives")]);
    }
}
