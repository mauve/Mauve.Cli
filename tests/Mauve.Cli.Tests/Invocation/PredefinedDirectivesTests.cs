using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
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

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-007")]
    public async Task Suggest_WritesOrderedCandidatesAndStopsBeforeCommand()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int handlers = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(PredefinedDirectives.CreateSuggest(output, errors))
            .AddChild(new CommandBuilder("add").SetHandler((_, _) => { handlers++; return 0; }))
            .AddChild(new CommandBuilder("admin")));
        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["[suggest:7]", "tool ad"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual($"add{Environment.NewLine}admin{Environment.NewLine}", output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
        Assert.AreEqual(0, handlers);
        Assert.AreEqual(ParseOutcome.Invalid, result.ParseResult!.Outcome);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-007")]
    public async Task Suggest_ReportsInvalidAndFailedCompletionWithoutPartialOutput()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition option = new("--target", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(option, static (_, _) => FailingValues())
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(PredefinedDirectives.CreateSuggest(output, errors, providers: providers))
            .AddOption(option));
        InvocationResult failed = await new CliInvoker().InvokeAsync(
            model, ["[suggest:14]", "tool --target="], TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult invalid = await new CliInvoker().InvokeAsync(
            model, ["[suggest:500]", "tool x"], TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult missing = await new CliInvoker().InvokeAsync(
            model, ["[suggest:0]"], TestContext.CancellationToken).ConfigureAwait(false);
        const string invalidContext = "tool --unknown ";
        InvocationResult contextFailed = await new CliInvoker().InvokeAsync(
            model, [$"[suggest:{invalidContext.Length}]", invalidContext], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(2, failed.ExitCode);
        Assert.AreEqual(2, invalid.ExitCode);
        Assert.AreEqual(2, missing.ExitCode);
        Assert.AreEqual(2, contextFailed.ExitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(errors.ToString().Contains("ProviderFailed", StringComparison.Ordinal));
        Assert.IsTrue(errors.ToString().Contains("Invalid completion", StringComparison.Ordinal));
        Assert.IsTrue(errors.ToString().Contains("requires a command-line payload", StringComparison.Ordinal));
        Assert.IsTrue(errors.ToString().Contains("ContextFailed", StringComparison.Ordinal));
        Assert.IsFalse(errors.ToString().Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-007")]
    public async Task Suggest_ForwardsProviderAndCancellation()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        using CancellationTokenSource cancellation = new();
        OptionDefinition option = new("--target", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(option, static (_, token) => WaitingValues(token))
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(PredefinedDirectives.CreateSuggest(output, errors, providers: providers))
            .AddOption(option));
        ValueTask<InvocationResult> pending = new CliInvoker().InvokeAsync(
            model, ["[suggest:14]", "tool --target="], cancellation.Token);
        await cancellation.CancelAsync().ConfigureAwait(false);
        InvocationResult result = await pending.ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-007")]
    public async Task Suggest_UsesProvidersAndRejectsIncompleteOrMultilineResults()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition option = new("--target", typeof(string), OptionArity.RequiredScalar);
        CompletionProviderRegistry good = new CompletionProviderRegistryBuilder()
            .Register(option, static (_, _) => Values("alpha", "beta"))
            .Build();
        CompletionProviderRegistry multiline = new CompletionProviderRegistryBuilder()
            .Register(option, static (_, _) => Values("first", "bad\nsecond"))
            .Build();
        CompletionProviderRegistry excessive = new CompletionProviderRegistryBuilder()
            .Register(option, static (_, _) => ManyValues())
            .Build();

        async Task<InvocationResult> Invoke(CompletionProviderRegistry providers, string payload)
        {
            CommandModel model = CommandModel.Create(new CommandBuilder("tool")
                .AddDirective(PredefinedDirectives.CreateSuggest(output, errors, providers: providers))
                .AddOption(option));
            return await new CliInvoker().InvokeAsync(
                model, [$"[suggest:{payload.Length}]", payload], TestContext.CancellationToken).ConfigureAwait(false);
        }

        InvocationResult success = await Invoke(good, "tool --target=a").ConfigureAwait(false);
        Assert.AreEqual(0, success.ExitCode);
        Assert.AreEqual($"alpha{Environment.NewLine}", output.ToString());
        output.GetStringBuilder().Clear();
        InvocationResult noMatches = await Invoke(good, "tool --target=z").ConfigureAwait(false);
        Assert.AreEqual(0, noMatches.ExitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        InvocationResult bad = await Invoke(multiline, "tool --target=").ConfigureAwait(false);
        InvocationResult limited = await Invoke(excessive, "tool --target=").ConfigureAwait(false);
        Assert.AreEqual(2, bad.ExitCode);
        Assert.AreEqual(2, limited.ExitCode);
        Assert.AreEqual(string.Empty, output.ToString());
        Assert.IsTrue(errors.ToString().Contains("multiline", StringComparison.Ordinal));
        Assert.IsTrue(errors.ToString().Contains("LimitReached", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-007")]
    public async Task Suggest_IgnoresAllDirectiveSpanTokensWhenFindingPayload()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        DirectiveValueDefinition value = new("value", typeof(string));
        DirectiveBuilder other = new DirectiveBuilder("other")
            .AddValue(value)
            .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(PredefinedDirectives.CreateSuggest(output, errors))
            .AddDirective(other)
            .AddChild(new CommandBuilder("add")));

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["[suggest:7]", "[other:", "value]", "tool ad"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual($"add{Environment.NewLine}", output.ToString());
    }

    private static async IAsyncEnumerable<CompletionValue> FailingValues()
    {
        yield return new CompletionValue("first");
        await Task.Yield();
        throw new InvalidOperationException("private");
    }

    private static async IAsyncEnumerable<CompletionValue> WaitingValues(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        yield return new CompletionValue("unreachable");
    }

    private static async IAsyncEnumerable<CompletionValue> Values(params string[] values)
    {
        foreach (string value in values)
        {
            yield return new CompletionValue(value);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<CompletionValue> ManyValues()
    {
        for (int index = 0; index < 130; index++)
        {
            yield return new CompletionValue($"item-{index:D3}");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
