using System;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace XmlDocumentation.Manual;

internal static class Program
{
    private static Task<int> Main(string[] args) => CliInvoker.RunAsync(CreateModel(), args);

    private static CommandModel CreateModel()
    {
        OptionDefinition text = new("--text", typeof(string), OptionArity.RequiredScalar,
            isRequired: true, description: "The text of the new item.");
        OptionDefinition dryRun = new("--dry-run", typeof(bool), OptionArity.BooleanFlag,
            description: "Attribute description wins.");
        PositionalDefinition category = new("category", typeof(string), minimumValues: 0,
            allowsNull: true, description: "An optional category for the item.");
        CommandBuilder add = new("add")
        {
            Description = "Add a todo item.",
            LongDescription = "Attribute metadata overrides XML text when both are present."
        };
        _ = add.AddOption(text).AddOption(dryRun).AddPositional(category)
            .AddExample("add --text AAA")
            .AddExample("$0 add --text \"Buy milk\" work")
            .SetHandler((context, _) =>
            {
                string selectedCategory = context.TryGetPositionalValue(category, out string? value) ? value! : "(none)";
                if (!context.TryGetOptionValue(text, out string? itemText)
                    || !context.TryGetOptionValue(dryRun, out bool isDryRun))
                {
                    throw new InvalidOperationException("Validated item options are unavailable.");
                }

                Console.WriteLine($"Add: {itemText}; category: {selectedCategory}; dry run: {isDryRun}");
                return 0;
            });
        CommandBuilder item = new CommandBuilder("item")
        {
            Description = "Work with todo items."
        }.AddChild(add);
        OptionPackBuilder output = new OptionPackBuilder()
            .AddOption(new OptionDefinition("verbose", typeof(bool), OptionArity.BooleanFlag,
                description: "Enable verbose output.", scope: OptionScope.Inherited));
        CommandBuilder root = new CommandBuilder("todo")
        {
            Description = "Manage todo items.",
            LongDescription = "Help metadata and invocation examples come from XML comments."
        }.AddExample("$0 --help").AddPack("output", output, "Output", "Shared output options.")
            .AddChild(item);
        return CommandModel.Create(root, includeStandardHelp: true);
    }
}
