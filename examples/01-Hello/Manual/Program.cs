using System;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Hello.Manual;

internal static class Program
{
    private static int Main(string[] args)
    {
        CommandBuilder root = new CommandBuilder("hello")
            .SetHandler((_, _) =>
            {
                Console.WriteLine("Hello, world!");
                return 0;
            });
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        return CliInvoker.Run(model, args);
    }

}
