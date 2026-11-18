using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;

namespace Todo.Cli;

[CliCommand("todo", IsRoot = true, Description = "Manage a small task list.")]
[CliChild(typeof(AddCommand), 0)]
[CliChild(typeof(ListCommand), 1)]
public partial class TodoCommand
{
}

[CliCommand("add", Aliases = ["new"], Description = "Add a task.")]
public partial class AddCommand
{
    [CliArgument("text", 0, Description = "Task text.")]
    public partial string Text { get; }

    [CliHandler]
    private int Run() => Write($"Added: {Text}");

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static int Write(string message)
    {
        Console.WriteLine(message);
        return 0;
    }
}

[CliCommand("list", Description = "List tasks.")]
public partial class ListCommand
{
    [CliHandler]
    private int Run() => Write("No tasks yet.");

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static int Write(string message)
    {
        Console.WriteLine(message);
        return 0;
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
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
