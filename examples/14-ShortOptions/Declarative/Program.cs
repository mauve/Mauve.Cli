using System;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace ShortOptions.Declarative;

[CliCommand("short-options", IsRoot = true, Description = "Combine short options and count repeated options.")]
internal sealed partial class ShortOptionsCommand
{
    [CliOption("verbose", ShortName = 'v', Arity = OptionArity.Counting, Description = "Increase verbosity.")]
    public partial int Verbosity { get; }

    [CliOption("all", ShortName = 'a', Description = "Include all items.")]
    public partial bool All { get; }

    [CliOption("force", ShortName = 'f', Description = "Apply changes without prompting.")]
    public partial bool Force { get; }

    [CliHandler]
    private int Run()
    {
        Console.WriteLine($"verbosity={Verbosity}; all={All}; force={Force}");
        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args) => CliInvoker.Run<ShortOptionsCommand>(args);
}
