using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Examples.Aot;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace Todo.Cli;

[CliCommand("todo", IsRoot = true, Description = "Manage a small task list.")]
[CliChild(typeof(AddCommand), 0)]
[CliChild(typeof(ListCommand), 1)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated model factory does not instantiate the root declaration.")]
internal sealed partial class TodoCommand
{
}

[CliCommand("add", Aliases = ["new"], Description = "Add a task.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class AddCommand
{
    [CliArgument("text", 0, Description = "Task text.")]
    public partial string Text { get; }

    [CliHandler]
    internal int Run()
    {
        return Write($"Added: {Text}");
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    internal static int Write(string message)
    {
        Console.WriteLine(message);
        return 0;
    }
}

[CliCommand("list", Description = "List tasks.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class ListCommand
{
    [CliHandler]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Generated command handlers are instance methods.")]
    internal int Run()
    {
        return Write("No tasks yet.");
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    internal static int Write(string message)
    {
        Console.WriteLine(message);
        return 0;
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args is ["--verify-aot"])
        {
            ConverterRegistry converters = AotScenario.CreateConverters();
            CommandModel aotModel = AotRootCommand.CreateModel(includeStandardHelp: true);
            ScenarioHandles handles = AotScenario.CreateGeneratedHandles(
                aotModel,
                GlobalPack.Option0,
                GlobalPack.Option1,
                NetworkPack.Option0,
                NetworkPack.Option1,
                NetworkPack.Option2,
                NetworkPack.Option3,
                NetworkPack.Option4,
                RetryPack.Option0);
            await AotScenario.VerifyAsync(aotModel, handles, converters).ConfigureAwait(false);
            Write("AOT scenarios passed.");
            return 0;
        }

        CommandModel model = TodoCommand.CreateModel(includeStandardHelp: true);
        InvocationResult result = await new CliInvoker().InvokeAsync(model, args).ConfigureAwait(false);
        if (result.Outcome == InvocationOutcome.HelpRequested && result.ParseResult?.SelectedCommand is not null)
        {
            Write(HelpTextRenderer.Render(HelpDocument.Create(model, result.ParseResult.SelectedCommand)));
            return 0;
        }

        if (result.Outcome == InvocationOutcome.InvalidInput && result.ParseResult is not null)
        {
            foreach (ParseDiagnostic diagnostic in result.ParseResult.Diagnostics)
            {
                Write(diagnostic.Message);
            }

            return 2;
        }

        return result.ExitCode ?? 1;
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static void Write(string message)
    {
        Console.WriteLine(message);
    }
}
