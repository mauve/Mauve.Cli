using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace Multicall.Declarative;

[CliCommand("multicall", IsRoot = true, Aliases = ["suite", "multicall-declarative"],
    Description = "One binary with several invocation names.")]
[CliChild<GreetCommand>]
internal sealed partial class MulticallCommand
{
}

[CliCommand("greet", Aliases = ["hi"], Description = "Greet someone.")]
[CliChild<FormalCommand>]
internal sealed partial class GreetCommand
{
    [CliArgument]
    public required string Name { get; init; }

    [CliHandler]
    internal void Run() => Console.WriteLine($"Hello, {Name}!");
}

[CliCommand("formal", Description = "Use a formal greeting.")]
internal sealed partial class FormalCommand
{
    [CliArgument]
    public required string Name { get; init; }

    [CliHandler]
    internal void Run() => Console.WriteLine($"Good day, {Name}.");
}

[CliCommand("farewell", IsRoot = true, Description = "A separate root in the same binary.")]
internal sealed partial class FarewellCommand
{
    [CliArgument]
    public required string Name { get; init; }

    [CliHandler]
    internal void Run() => Console.WriteLine($"Goodbye, {Name}!");
}

internal static class Program
{
    private static Task<int> Main(string[] args)
        => CliInvoker.DispatchMulticallAsync(
            [MulticallCommand.CreateModel(includeStandardHelp: true), FarewellCommand.CreateModel(includeStandardHelp: true)],
            routes: [new CliMulticallRoute("smurf", ["multicall", "greet"])],
            arguments: args);
}
