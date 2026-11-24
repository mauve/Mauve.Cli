using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Multicall.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        PositionalDefinition greetName = new("name", typeof(string));
        PositionalDefinition formalName = new("name", typeof(string));
        PositionalDefinition farewellName = new("name", typeof(string));
        CommandModel multicall = CommandModel.Create(new CommandBuilder("multicall")
            .AddAlias("suite").AddAlias("multicall-manual")
            .AddChild(new CommandBuilder("greet").AddAlias("hi")
                .AddPositional(greetName)
                .SetHandler((context, _) => Console.WriteLine($"Hello, {GetName(context, greetName)}!"))
                .AddChild(new CommandBuilder("formal")
                    .AddPositional(formalName)
                    .SetHandler((context, _) => Console.WriteLine($"Good day, {GetName(context, formalName)}.")))),
            includeStandardHelp: true);
        CommandModel farewell = CommandModel.Create(new CommandBuilder("farewell")
            .AddPositional(farewellName)
            .SetHandler((context, _) => Console.WriteLine($"Goodbye, {GetName(context, farewellName)}!")),
            includeStandardHelp: true);
        return CliInvoker.DispatchMulticallAsync([multicall, farewell],
            routes: [new CliMulticallRoute("smurf", ["multicall", "greet"])],
            arguments: args);
    }

    private static string GetName(CommandInvocationContext context, PositionalDefinition definition)
        => context.TryGetPositionalValue(definition, out string? name)
            ? name!
            : throw new InvalidOperationException("The validated name is unavailable.");
}
