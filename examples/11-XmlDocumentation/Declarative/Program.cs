using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace XmlDocumentation.Declarative;

/// <summary>Manage todo items.</summary>
/// <remarks>Help metadata and invocation examples come from XML comments.</remarks>
/// <example><code>$0 --help</code></example>
[CliCommand("todo", IsRoot = true)]
[CliChild<ItemCommand>]
internal sealed partial class TodoCommand(OutputPack output)
{
    public OutputPack Output { get; } = output;
}

/// <summary>Work with todo items.</summary>
[CliCommand]
[CliChild<AddCommand>]
internal sealed partial class ItemCommand
{
}

/// <summary>Add a todo item.</summary>
/// <remarks>Attribute metadata overrides XML text when both are present.</remarks>
/// <example><code>add --text AAA</code></example>
/// <example><code language="shell">$0 add --text "Buy milk" work</code></example>
/// <example><code language="csharp">
/// CommandModel model = TodoCommand.CreateModel();
/// </code></example>
[CliCommand]
internal sealed partial class AddCommand
{
    /// <summary>The text of the new item.</summary>
    [CliOption(IsRequired = true)]
    public partial string Text { get; }

    /// <summary>This XML description is overridden.</summary>
    [CliOption(Description = "Attribute description wins.")]
    public partial bool DryRun { get; }

    /// <summary>An optional category for the item.</summary>
    [CliArgument(MinimumValues = 0)]
    public partial string? Category { get; }

    [CliHandler]
    private int Run()
    {
        string category = TryGetCategory(out string? value) ? value! : "(none)";
        Console.WriteLine($"Add: {Text}; category: {category}; dry run: {DryRun}");
        return 0;
    }
}

/// <summary>Output</summary>
/// <remarks>Shared output options.</remarks>
[CliOptionPack]
internal sealed partial class OutputPack
{
    /// <summary>Enable verbose output.</summary>
    [CliOption(Scope = OptionScope.Inherited)]
    public partial bool Verbose { get; }
}

internal static class Program
{
    private static Task<int> Main(string[] args) => CliInvoker.RunAsync<TodoCommand>(args);
}
