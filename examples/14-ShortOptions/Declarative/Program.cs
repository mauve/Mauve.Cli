using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ShortOptions.Declarative;

[CliCommand("short-options", IsRoot = true, Description = "Combine short options and count repeated options.")]
internal sealed partial class ShortOptionsCommand
{
    [CliOption("verbose", ShortName = 'v', Arity = OptionArity.Counting, Description = "Increase verbosity.")]
    public int Verbosity { get; init; }

    [CliOption("all", ShortName = 'a', Description = "Include all items.")]
    public bool All { get; init; }

    [CliOption("force", ShortName = 'f', Description = "Apply changes without prompting.")]
    public bool Force { get; init; }

    [CliHandler]
    internal int Run()
    {
        Console.WriteLine($"verbosity={Verbosity}; all={All}; force={Force}");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ShortOptionsCommand>(args);
}
