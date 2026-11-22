using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Commands.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        PositionalDefinition greetName = new("name", typeof(string), description: "Person to greet.");
        PositionalDefinition goodbyeName = new("name", typeof(string), description: "Person to address.");
        CommandBuilder root = new CommandBuilder("commands")
            .AddDirective(PredefinedDirectives.CreateDebug())
            .AddDirective(PredefinedDirectives.CreateCompletion("commands"))
            .AddChild(new CommandBuilder("greet")
                .AddAlias("hi")
                .AddPositional(greetName)
                .SetHandler((context, _) =>
                {
                    string? name = context.TryGetPositionalValue(greetName, out string? value)
                        ? value
                        : throw new InvalidOperationException("The greet name is unavailable.");
                    Console.WriteLine($"Hello, {name}!");
                    return 0;
                }))
            .AddChild(new CommandBuilder("goodbye")
                .AddPositional(goodbyeName)
                .SetHandler((context, _) =>
                {
                    string? name = context.TryGetPositionalValue(goodbyeName, out string? value)
                        ? value
                        : throw new InvalidOperationException("The goodbye name is unavailable.");
                    Console.WriteLine($"Goodbye, {name}!");
                    return 0;
                }));
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        return CliInvoker.RunAsync(model, args);
    }

}
