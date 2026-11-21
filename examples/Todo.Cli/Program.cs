// User-facing example of generated nested commands and handler binding.
using System;
using System.Diagnostics.CodeAnalysis;
using Mauve.Cli.Declarations;
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
    private static int Main() => CliInvoker.Run<TodoCommand>();
}
