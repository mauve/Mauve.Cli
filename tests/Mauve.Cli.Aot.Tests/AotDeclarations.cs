using System;
using System.Diagnostics.CodeAnalysis;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;

namespace Mauve.Cli.Aot.Tests;

[CliCommand("aot-todo", IsRoot = true)]
[CliChild<AotAddCommand>]
[CliPack<GlobalPack>("global", Title = "Global")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This generated declaration supplies the static model factory.")]
internal sealed partial class AotRootCommand
{
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
    private int Run() => AotDirectiveState.RecordDebug(Text);
}

[CliCommand("add", Aliases = ["new"])]
[CliPack<NetworkPack>("network", Title = "Network")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "The generated dispatcher instantiates this command at invocation.")]
internal sealed partial class AotAddCommand
{
    [CliOption(IsRequired = true)]
    public partial int Count { get; }

    [CliArgument]
    public partial string Text { get; }

    [CliHandler]
    private int Run(CommandInvocationContext context)
    {
        return AotScenario.HandleGenerated(
            context,
            Count,
            Text,
            GlobalPack.Option0,
            GlobalPack.Option1,
            NetworkPack.Option0,
            NetworkPack.Option1,
            NetworkPack.Option2,
            NetworkPack.Option3,
            NetworkPack.Option4,
            RetryPack.Option0);
    }
}

[CliOptionPack]
[CliPackOption<string>(
    "--workspace",
    IsRequired = true,
    Scope = OptionScope.Inherited)]
[CliPackOption<bool>("--verbose", Scope = OptionScope.Inherited)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class GlobalPack
{
}

[CliOptionPack]
[CliPack<RetryPack>("retry", Title = "Retry")]
[CliPackOption<string>("--tag", IsRequired = true)]
[CliPackOption<Priority>("--priority", IsRequired = true)]
[CliPackOption<Priority?>("--backup-priority", IsRequired = true)]
[CliPackOption<BuildCode>("--code", IsRequired = true)]
[CliPackOption<int?>("--limit", IsRequired = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class NetworkPack
{
}

[CliOptionPack]
[CliPackOption<int>("--retries", IsRequired = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class RetryPack
{
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
