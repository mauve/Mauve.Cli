using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace Hello.Declarative;

[CliCommand(IsRoot = true, Description = "Print a friendly greeting.")]
internal sealed partial class HelloCommand
{
    [CliHandler]
    internal int Run()
    {
        Console.WriteLine("Hello, world!");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<HelloCommand>(args);
}
