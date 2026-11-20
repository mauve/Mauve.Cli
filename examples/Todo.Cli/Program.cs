// User-facing example of generated nested commands and handler binding.
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;

namespace Todo.Cli;

[CliCommand(IsRoot = true, Description = "Manage a small task list.")]
[CliChild<AddCommand>]
[CliChild<ListCommand>]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated model factory does not instantiate the root declaration.")]
internal sealed partial class TodoCommand
{
}

[CliCommand(Aliases = ["new"], Description = "Add a task.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class AddCommand
{
    [CliArgument(Description = "Task text.")]
    public partial string Text { get; }

    [CliHandler]
    internal int Run() => Write($"Added: {Text}");

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

[CliCommand(Description = "List tasks.")]
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
    internal int Run() => Write("No tasks yet.");

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
        CommandModel model = TodoCommand.CreateModel(includeStandardHelp: true);
        if (args is ["--verify-aot"])
        {
            await VerifyAotAsync(model).ConfigureAwait(false);
            Write("AOT smoke check passed.");
            return 0;
        }

        InvocationResult result = await new CliInvoker().InvokeAsync(model, args).ConfigureAwait(false);
        if (result.IsHelpRequested)
        {
            Write(HelpTextRenderer.Render(HelpDocument.Create(model, result.SelectedCommand)));
            return 0;
        }

        if (result.IsInvalidInput)
        {
            foreach (string line in result.FormatDiagnostics())
            {
                await Console.Error.WriteLineAsync(line).ConfigureAwait(false);
            }

            return 2;
        }

        return result.ExitCode ?? 1;
    }

    private static async Task VerifyAotAsync(CommandModel model)
    {
        CliInvoker invoker = new();
        InvocationResult valid = await invoker.InvokeAsync(model, ["new", "AOT check"]).ConfigureAwait(false);
        if (valid.Outcome != InvocationOutcome.Completed
            || valid.ExitCode != 0
            || string.Join(" ", valid.ParseResult!.SelectedCommand!.Path) != "todo add")
        {
            throw new InvalidOperationException("The generated Todo command did not run successfully.");
        }

        InvocationResult invalid = await invoker.InvokeAsync(model, ["add"]).ConfigureAwait(false);
        if (invalid.Outcome != InvocationOutcome.InvalidInput)
        {
            throw new InvalidOperationException("The generated Todo command accepted missing input.");
        }
    }

    [SuppressMessage(
        "Globalization",
        "CA1303:Do not pass literals as localized parameters",
        Justification = "This small example uses English output.")]
    private static void Write(string message) => Console.WriteLine(message);
}
