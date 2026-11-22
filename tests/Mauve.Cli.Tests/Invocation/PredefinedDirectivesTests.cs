using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class PredefinedDirectivesTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-006")]
    public async Task Debug_WaitsForAttachmentBeforeContinuing()
    {
        int polls = 0;
        int waits = 0;
        int handlers = 0;
        DirectiveBuilder debug = PredefinedDirectives.CreateDebug(
            () => ++polls >= 3,
            _ =>
            {
                waits++;
                return Task.CompletedTask;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(debug)
            .SetHandler((_, _) => { handlers++; return 0; }));

        InvocationResult result = await new CliInvoker().InvokeAsync(model, ["[debug]"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(2, waits);
        Assert.AreEqual(1, handlers);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-006")]
    public async Task Debug_CancellationStopsWaitingAndLaterHandlers()
    {
        int handlers = 0;
        using CancellationTokenSource cancellation = new();
        DirectiveBuilder debug = PredefinedDirectives.CreateDebug(
            static () => false,
            static token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(debug)
            .SetHandler((_, _) => { handlers++; return 0; }));
        ValueTask<InvocationResult> pending = new CliInvoker().InvokeAsync(model, ["[debug]"], cancellation.Token);
        await cancellation.CancelAsync().ConfigureAwait(false);
        InvocationResult result = await pending.ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, handlers);
    }
}
