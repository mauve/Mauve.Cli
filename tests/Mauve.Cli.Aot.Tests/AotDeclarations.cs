using System;
using System.Diagnostics.CodeAnalysis;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Mauve.Cli.Aot.Tests;

[CliCommand("aot-todo", IsRoot = true)]
[CliChild<AotAddCommand>]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This generated declaration supplies the static model factory.")]
internal sealed partial class AotRootCommand(GlobalPack global)
{
    public GlobalPack Global { get; } = global;
}

[CliCommand("directive", IsRoot = true)]
[CliDirective<DebugDirective>]
[SuppressMessage(
    "Style",
    "IDE0051:Remove unused private members",
    Justification = "The generated directive descriptor references the root method.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This generated declaration supplies the static model factory.")]
internal sealed partial class AotDirectiveRootCommand
{
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The generated directive binding invokes root instance methods.")]
    [CliDirective("version")]
    private int VersionDirective() => AotDirectiveState.RecordVersion();
}

[CliDirective]
[SuppressMessage(
    "Style",
    "IDE0051:Remove unused private members",
    Justification = "The generated directive descriptor references the handler.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated directive dispatcher instantiates this declaration.")]
internal sealed partial class DebugDirective
{
    [CliOption(IsRequired = true)]
    public partial string Text { get; }

    [CliHandler]
    internal int Run() => AotDirectiveState.RecordDebug(Text);
}

/// <summary>Add an AOT scenario item.</summary>
/// <example><code>add --count 2 AAA</code></example>
/// <example><code language="shell">$0 add --count 2 AAA</code></example>
/// <example><code language="csharp">new object();</code></example>
[CliCommand("add", Aliases = ["new"])]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class AotAddCommand(NetworkPack network)
{
    [CliOption(IsRequired = true)]
    public int Count { get; init; }

    /// <summary>The item text.</summary>
    [CliArgument]
    public required string Text { get; init; }

    [CliHandler]
    internal int Run(CommandInvocationContext context)
    {
        return AotScenario.HandleGenerated(
            context,
            Count,
            Text,
            network);
    }
}

[CliOptionPack(Title = "Global", Description = "Global AOT scenario options.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated binding instantiates this pack when needed.")]
internal sealed partial class GlobalPack
{
    [CliOption("--workspace", IsRequired = true, Scope = OptionScope.Inherited)]
    public required string Workspace { get; init; }

    [CliOption("--verbose", Scope = OptionScope.Inherited)]
    public bool Verbose { get; init; }
}

[CliOptionPack(Title = "Network", Description = "Network AOT scenario options.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated binding instantiates this pack for each invocation.")]
internal sealed partial class NetworkPack(RetryPack retry)
{
    public RetryPack Retry { get; } = retry;

    [CliOption("--tag", IsRequired = true)]
    public required string Tag { get; init; }

    [CliOption("--priority", IsRequired = true)]
    public Priority Priority { get; init; }

    [CliOption("--backup-priority", IsRequired = true)]
    public Priority? BackupPriority { get; init; }

    [CliOption("--code", IsRequired = true)]
    public BuildCode Code { get; init; }

    [CliOption("--limit", IsRequired = true)]
    public int? Limit { get; init; }
}

[CliOptionPack(Title = "Retry", Description = "Retry AOT scenario options.")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated binding instantiates this nested pack for each invocation.")]
internal sealed partial class RetryPack
{
    [CliOption("--retries", IsRequired = true)]
    public int Retries { get; init; }
}

internal static class AotDirectiveState
{
    private static int _stage;

    internal static string? CapturedText { get; private set; }

    internal static void Reset()
    {
        _stage = 0;
        CapturedText = null;
    }

    internal static int RecordDebug(string text)
    {
        if (_stage != 0)
        {
            throw new InvalidOperationException("The generated directive order changed.");
        }

        CapturedText = text;
        _stage = 1;
        return 0;
    }

    internal static int RecordVersion()
    {
        if (_stage != 1)
        {
            throw new InvalidOperationException("The generated directive order changed.");
        }

        _stage = 2;
        return 0;
    }

    internal static bool IsComplete => _stage == 2;
}
