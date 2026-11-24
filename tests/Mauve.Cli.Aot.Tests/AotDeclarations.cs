using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Validation;

namespace Mauve.Cli.Aot.Tests;

[CliCommand("multicall-aot", IsRoot = true, Aliases = ["suite-aot"])]
[CliChild<AotMulticallCopyCommand>]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated declaration supplies a static multicall model factory.")]
internal sealed partial class AotMulticallRootCommand
{
}

[CliCommand("copy", Aliases = ["cp"])]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated multicall dispatcher constructs this command.")]
internal sealed partial class AotMulticallCopyCommand
{
    [CliArgument]
    public required string Name { get; init; }

    [CliHandler]
    internal int Run(CommandInvocationContext context) => Name.Length + context.ParseResult.SelectedPath.Count;
}

[CliCommand("exception-aot", IsRoot = true)]
internal sealed partial class AotExceptionCommand
{
    [CliOption]
    public bool Direct { get; init; }

    [CliHandler]
    internal int Run() => Direct
        ? throw new CliExitCodeException(75, "Direct native failure.")
        : throw new FileNotFoundException("Private file context.");
}

[CliCommand("validation-aot", IsRoot = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated validation scenario constructs this command.")]
internal sealed partial class AotValidationCommand
{
    [CliOption(IsRequired = true, Arity = OptionArity.RepeatedSingleCollection)]
    [CliLength(2, 8, Target = CliValidationTarget.Items)]
    [CliValidator<AotTagValidator>(Target = CliValidationTarget.Items)]
    public required IReadOnlyList<string> Tags { get; init; }

    [CliOption]
    [CliRange(1, 10)]
    public int Count { get; init; } = 3;

    [CliHandler]
    internal int Run() => Count + Tags.Count;
}

internal readonly struct AotTagValidator : ICliValidator<string>
{
    public static ValueValidationOutcome Validate(string? value, ValidationContext context) =>
        value == "bad"
            ? throw new CliValidationException(ValueValidationOutcome.Failure("tag.bad"))
            : ValueValidationOutcome.Success;
}

[CliCommand("environment-aot", IsRoot = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated environment-binding scenario is invoked by its acceptance test.")]
internal sealed partial class AotEnvironmentCommand
{
    [CliOption(IsRequired = true, EnvironmentVariableName = "AOT_NAME")]
    public required string Name { get; init; }

    [CliHandler]
    internal int Run() => Name.Length;
}

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
