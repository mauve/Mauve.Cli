using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace HelpAndErrors.Declarative;

/// <summary>Inspect a numbered item.</summary>
/// <remarks>
/// Use this command to inspect a numbered item and explore the help output.
/// Choose a safe or fast inspection mode, add tags to describe the item, and
/// use verbose output when you need more detail. The show and summary
/// subcommands demonstrate inherited options. The notice directive runs
/// before command processing begins.
/// </remarks>
/// <example><code>$0 --count=4 --mode=safe</code></example>
/// <example><code language="shell">$0 '[notice]' --count=4 --mode=fast</code></example>
[CliCommand("inspect", IsRoot = true)]
[CliChild<ShowCommand>]
[CliChild<SummaryCommand>]
[CliDirective<NoticeDirective>]
internal sealed partial class InspectCommand(InspectionOptions inspection)
{
    [CliOption("count", IsRequired = true, Description = "Item number.")]
    public int Count { get; init; }

    [CliOption("tag", Arity = OptionArity.RepeatedSingleCollection, Description = "Tag the item.")]
    public IReadOnlyList<string>? Tags { get; init; }

    [CliOption("verbose", Scope = OptionScope.Inherited, Description = "Show detailed output.")]
    public bool Verbose { get; init; }

    [CliHandler]
    internal int Run()
    {
        if (Count < 0)
        {
            throw new CliExitCodeException(65, "The item number must not be negative.");
        }

        if (Count == 0)
        {
            throw new FileNotFoundException("Internal lookup details, not safe for public output.");
        }

        Console.WriteLine($"Item: {Count}; mode: {inspection.Mode}; tags: {string.Join(", ", Tags ?? [])}; verbose: {Verbose}");
        return 0;
    }
}

[CliDirective(Description = "Print a notice before command execution.")]
internal sealed partial class NoticeDirective
{
    [CliHandler]
    internal int Run(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("Notice directive ran.");
        return 0;
    }
}

[CliOptionPack(Title = "Inspection", Description = "Inspection settings.")]
internal sealed partial class InspectionOptions
{
    [CliOption("mode", Description = "Inspection mode.")]
    [CliAllowedValues("safe", "fast")]
    public string Mode { get; init; } = "safe";
}

[CliCommand("show", Description = "Show an item.")]
internal sealed partial class ShowCommand
{
    [CliHandler]
    internal int Run(CommandInvocationContext context) => ChildOutput.Write(context, "Show");
}

[CliCommand("summary", Description = "Summarize items.")]
internal sealed partial class SummaryCommand
{
    [CliHandler]
    internal int Run(CommandInvocationContext context) => ChildOutput.Write(context, "Summary");
}

internal static class ChildOutput
{
    public static int Write(CommandInvocationContext context, string label)
    {
        OptionDefinition verbose = context.ParseResult.SelectedPath[0].Options
            .Single(static option => option.Name == "--verbose");
        if (!context.TryGetOptionValue(verbose, out bool value))
        {
            throw new InvalidOperationException("Validated input has no verbosity setting.");
        }

        Console.WriteLine($"{label}; verbose: {value}");
        return 0;
    }
}

internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        CliHostOptions options = new CliHostOptionsBuilder()
            .MapException<IOException>(74, "The requested item is unavailable.")
            .WithExceptionRenderer(static context => "inspect: " + context.Result.SafeMessage)
            .Build();
        return CliInvoker.RunAsync<InspectCommand>(args, options);
    }
}
