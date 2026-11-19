// Internal FEAT-13 fixture for generated-model trimming and NativeAOT checks.
using System.Diagnostics.CodeAnalysis;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Examples.Aot;
using Mauve.Cli.Invocation;

namespace Todo.Cli;

[CliCommand("aot-todo", IsRoot = true)]
[CliChild(typeof(AotAddCommand), 0)]
[CliPack(typeof(GlobalPack), "global", 0, Title = "Global")]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This generated declaration supplies the static model factory.")]
internal sealed partial class AotRootCommand
{
}

[CliCommand("add", Aliases = ["new"])]
[CliPack(typeof(NetworkPack), "network", 0, Title = "Network")]
internal sealed partial class AotAddCommand
{
    [CliOption("--count", OptionArity.RequiredScalar, 0, IsRequired = true)]
    public partial int Count { get; }

    [CliArgument("text", 0)]
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
[CliPackOption(
    "--workspace",
    typeof(string),
    OptionArity.RequiredScalar,
    0,
    IsRequired = true,
    Scope = OptionScope.Inherited)]
[CliPackOption("--verbose", typeof(bool), OptionArity.BooleanFlag, 1, Scope = OptionScope.Inherited)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class GlobalPack
{
}

[CliOptionPack]
[CliPack(typeof(RetryPack), "retry", 0, Title = "Retry")]
[CliPackOption("--tag", typeof(string), OptionArity.RequiredScalar, 0, IsRequired = true)]
[CliPackOption("--priority", typeof(Priority), OptionArity.RequiredScalar, 1, IsRequired = true)]
[CliPackOption("--backup-priority", typeof(Priority?), OptionArity.RequiredScalar, 2, IsRequired = true)]
[CliPackOption("--code", typeof(BuildCode), OptionArity.RequiredScalar, 3, IsRequired = true)]
[CliPackOption("--limit", typeof(int?), OptionArity.RequiredScalar, 4, IsRequired = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class NetworkPack
{
}

[CliOptionPack]
[CliPackOption("--retries", typeof(int), OptionArity.RequiredScalar, 0, IsRequired = true)]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Generated pack descriptors are static and are not instantiated.")]
internal sealed partial class RetryPack
{
}
