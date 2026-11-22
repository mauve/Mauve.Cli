using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Directives.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
        => CliInvoker.RunAsync(CreateModel(), args);

    private static CommandModel CreateModel()
    {
        DirectiveBuilder notice = new("notice")
        {
            Description = "Print a notice before command execution."
        };
        _ = notice.SetHandler((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine("Notice directive ran.");
            return 0;
        });
        return CommandModel.Create(
            new CommandBuilder("10-directives")
            {
                Description = "Demonstrate custom command directives."
            }
                .AddDirective(notice)
                .AddDirective(PredefinedDirectives.CreateCompletion("10-directives"))
                .AddChild(new CommandBuilder("run")
                {
                    Description = "Run a no-op status command."
                }
                    .SetHandler((_, _) =>
                    {
                        Console.WriteLine("Command ran.");
                        return 0;
                    })),
            includeStandardHelp: true);
    }

}
