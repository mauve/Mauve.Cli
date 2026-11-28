using System;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Invocation;

namespace InvocationArgvFixture;

internal static class Program
{
    private static async Task<int> Main()
    {
        return Environment.GetEnvironmentVariable("MAUVE_ARGV_MODE") switch
        {
            "sync-default" => RunSync(explicitArguments: false),
            "sync-explicit" => RunSync(explicitArguments: true),
            "async-default" => await CliInvoker.RunAsync<AsyncArgvCommand>().ConfigureAwait(false),
            "async-explicit" => await CliInvoker.RunAsync<AsyncArgvCommand>(["--number=29"]).ConfigureAwait(false),
            _ => 90
        };
    }

    private static int RunSync(bool explicitArguments) => explicitArguments
        ? CliInvoker.Run<SyncArgvCommand>(["--number=29"])
        : CliInvoker.Run<SyncArgvCommand>();
}

[CliCommand("sync-argv", IsRoot = true)]
public sealed partial class SyncArgvCommand
{
    [CliOption(IsRequired = true)]
    public int Number { get; init; }

    [CliHandler]
    public int Run() => Number;
}

[CliCommand("async-argv", IsRoot = true)]
public sealed partial class AsyncArgvCommand
{
    [CliOption(IsRequired = true)]
    public int Number { get; init; }

    [CliHandler]
    public async Task<int> RunAsync()
    {
        await Task.Yield();
        return Number;
    }
}
