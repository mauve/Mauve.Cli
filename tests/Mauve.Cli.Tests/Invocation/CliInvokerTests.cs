using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class CliInvokerTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-001")]
    public async Task InvokeAsync_DispatchesOnlyTheSelectedValidatedHandler()
    {
        int rootCalls = 0;
        int leafCalls = 0;
        OptionDefinition count = new("--count", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        CommandBuilder leaf = new CommandBuilder("copy")
            .AddOption(count)
            .SetHandler((context, _) =>
            {
                leafCalls++;
                Assert.IsTrue(context.TryGetOptionValue(count, out int value));
                Assert.AreEqual(7, value);
                return 23;
            });
        CommandBuilder root = new CommandBuilder("tool")
            .SetHandler((_, _) =>
            {
                rootCalls++;
                return 0;
            })
            .AddChild(leaf);
        CommandModel model = CommandModel.Create(root);

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["copy", "--count=7"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(23, result.ExitCode);
        Assert.AreEqual(0, rootCalls);
        Assert.AreEqual(1, leafCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-001")]
    public async Task InvokeAsync_NeverDispatchesInvalidOrHelpResultsAndReportsNoHandler()
    {
        int handlerCalls = 0;
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition(
                    "--source",
                    typeof(string),
                    OptionArity.RequiredScalar,
                    isRequired: true))
                .SetHandler((_, _) =>
                {
                    handlerCalls++;
                    return 0;
                }));
        CliInvoker invoker = new();

        InvocationResult invalid = await invoker
            .InvokeAsync(CommandModel.Create(root), ["copy", "--wat"], TestContext.CancellationToken)
            .ConfigureAwait(false);
        CommandModel helpModel = CommandModel.Create(root, includeStandardHelp: true);
        InvocationResult help = await invoker.InvokeAsync(helpModel, ["copy", "--help"], TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult noHandler = await invoker.InvokeAsync(
            CommandModel.Create(new CommandBuilder("empty")),
            [], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, invalid.Outcome);
        Assert.AreEqual(InvocationOutcome.HelpRequested, help.Outcome);
        Assert.AreEqual(InvocationOutcome.NoHandler, noHandler.Outcome);
        Assert.IsNull(noHandler.ExitCode);
        Assert.AreEqual(0, handlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-002")]
    public async Task InvokeAsync_NormalizesSupportedHandlerReturnShapes()
    {
        InvocationResult syncVoid = await Invoke(new CommandBuilder("sync-void")
            .SetHandler(static (_, _) => { })).ConfigureAwait(false);
        InvocationResult syncCode = await Invoke(new CommandBuilder("sync-code")
            .SetHandler(static (_, _) => 11)).ConfigureAwait(false);
        InvocationResult task = await Invoke(new CommandBuilder("task")
            .SetTaskHandler(static (_, _) => Task.CompletedTask)).ConfigureAwait(false);
        InvocationResult taskCode = await Invoke(new CommandBuilder("task-code")
            .SetTaskHandlerWithExitCode(static (_, _) => Task.FromResult(12))).ConfigureAwait(false);
        InvocationResult valueTask = await Invoke(new CommandBuilder("value-task")
            .SetValueTaskHandler(static (_, _) => ValueTask.CompletedTask)).ConfigureAwait(false);
        InvocationResult valueTaskCode = await Invoke(new CommandBuilder("value-task-code")
            .SetValueTaskHandlerWithExitCode(static (_, _) => ValueTask.FromResult(13))).ConfigureAwait(false);

        Assert.AreEqual(0, syncVoid.ExitCode);
        Assert.AreEqual(11, syncCode.ExitCode);
        Assert.AreEqual(0, task.ExitCode);
        Assert.AreEqual(12, taskCode.ExitCode);
        Assert.AreEqual(0, valueTask.ExitCode);
        Assert.AreEqual(13, valueTaskCode.ExitCode);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-003")]
    public async Task InvokeAsync_HonorsPreCancellationAndStopsBetweenParserCallbacks()
    {
        int factoryCalls = 0;
        int handlerCalls = 0;
        CommandModel preCancelledModel = CommandModel.Create(
            new CommandBuilder("tool")
                .AddOption(new OptionDefinition(
                    "--name",
                    typeof(string),
                    OptionArity.RequiredScalar,
                    defaultValuePolicy: DefaultValuePolicy.Factory,
                    defaultValueFactory: () =>
                    {
                        factoryCalls++;
                        return "default";
                    }))
                .SetHandler((_, _) =>
                {
                    handlerCalls++;
                    return 0;
                }));
        CancellationToken preCancelledToken = new(true);

        InvocationResult preCancelled = await new CliInvoker().InvokeAsync(
            preCancelledModel,
            [],
            preCancelledToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, preCancelled.Outcome);
        Assert.IsNull(preCancelled.ParseResult);
        Assert.AreEqual(0, factoryCalls);
        Assert.AreEqual(0, handlerCalls);

        using CancellationTokenSource converterSource = new();
        int validatorCalls = 0;
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register((_, _) =>
            {
                _ = converterSource.CancelAsync();
                return ValueConversion.FromValue(4);
            })
            .Build();
        ValueValidator<int> validator = new((_, _) =>
        {
            validatorCalls++;
            return ValueValidationOutcome.Success;
        });
        CommandModel converterModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                validators: [validator])));

        InvocationResult cancelledDuringParse = await new CliInvoker(new CliParser(converters)).InvokeAsync(
            converterModel,
            ["--count=4"],
            converterSource.Token).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, cancelledDuringParse.Outcome);
        Assert.IsNull(cancelledDuringParse.ParseResult);
        Assert.AreEqual(0, validatorCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-003")]
    public async Task InvokeAsync_ReportsCooperativeCancellationButPreservesCompletedExitCode()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CommandModel waitingModel = CommandModel.Create(new CommandBuilder("wait")
            .SetTaskHandler(async (context, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }));
        using CancellationTokenSource waitingSource = new();
        Task<InvocationResult> running = new CliInvoker().InvokeAsync(
            waitingModel,
            [],
            waitingSource.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
        await waitingSource.CancelAsync().ConfigureAwait(false);
        InvocationResult cancelled = await running.ConfigureAwait(false);

        using CancellationTokenSource lateSource = new();
        CommandModel completingModel = CommandModel.Create(new CommandBuilder("complete")
            .SetTaskHandlerWithExitCode(async (_, _) =>
            {
                await lateSource.CancelAsync().ConfigureAwait(false);
                return 42;
            }));
        InvocationResult completed = await new CliInvoker().InvokeAsync(
            completingModel,
            [],
            lateSource.Token).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, cancelled.Outcome);
        Assert.AreEqual(InvocationOutcome.Completed, completed.Outcome);
        Assert.AreEqual(42, completed.ExitCode);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-004")]
    public async Task InvokeAsync_PropagatesHandlerFaultsAndIsolatesInvocations()
    {
        OperationCanceledException expected = new("handler fault");
        CommandModel faultingModel = CommandModel.Create(new CommandBuilder("fault")
            .SetHandler((_, _) => throw expected));
        try
        {
            _ = await new CliInvoker().InvokeAsync(faultingModel, [], TestContext.CancellationToken).ConfigureAwait(false);
            Assert.Fail();
        }
        catch (OperationCanceledException actual)
        {
            Assert.AreSame(expected, actual);
        }

        InvalidOperationException unexpected = new("handler failure");
        CommandModel failingModel = CommandModel.Create(new CommandBuilder("fail")
            .SetHandler((_, _) => throw unexpected));
        try
        {
            _ = await new CliInvoker().InvokeAsync(failingModel, [], TestContext.CancellationToken).ConfigureAwait(false);
            Assert.Fail();
        }
        catch (InvalidOperationException actual)
        {
            Assert.AreSame(unexpected, actual);
        }

        ConcurrentBag<CommandInvocationContext> contexts = [];
        OptionDefinition value = new("--value", typeof(int), OptionArity.RequiredScalar);
        CommandModel concurrentModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(value)
            .SetHandler((context, _) =>
            {
                contexts.Add(context);
                return 0;
            }));
        CliInvoker invoker = new();
        InvocationResult[] results = await Task.WhenAll(
            invoker.InvokeAsync(concurrentModel, ["--value=1"], TestContext.CancellationToken).AsTask(),
            invoker.InvokeAsync(concurrentModel, ["--value=2"], TestContext.CancellationToken).AsTask()).ConfigureAwait(false);
        CommandInvocationContext[] captured = [.. contexts];

        Assert.AreEqual(2, results.Count(static result => result.Outcome == InvocationOutcome.Completed));
        Assert.AreNotSame(captured[0], captured[1]);
        Assert.AreNotSame(captured[0].ParseResult, captured[1].ParseResult);
        Assert.Contains(context =>
            context.TryGetOptionValue(value, out int parsed) && parsed == 1, captured);
        Assert.Contains(context =>
            context.TryGetOptionValue(value, out int parsed) && parsed == 2, captured);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-12")]
    [TestProperty(Metadata.Requirement, "FEAT-12-004")]
    public async Task InvokeAsync_ExposesInheritedValuesFromTheirDeclaringCommand()
    {
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(verbose)
            .AddChild(new CommandBuilder("copy")
                .SetHandler((context, _) =>
                {
                    Assert.IsTrue(context.TryGetOptionValue(verbose, out bool enabled));
                    Assert.IsTrue(enabled);
                    return 0;
                })));

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["--verbose", "copy"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public void Run_UsesConfiguredHostOutputAndExitCodes()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(
            output,
            errors,
            helpRenderer: static (_, _) => "custom help",
            invalidInputExitCode: 7,
            noHandlerExitCode: 8,
            cancelledExitCode: 9);

        int completed = CliInvoker.Run<SyncHostCommand>(["sync"], options, TestContext.CancellationToken);
        int help = CliInvoker.Run<SyncHostCommand>(["sync", "--help"], options, TestContext.CancellationToken);
        int invalid = CliInvoker.Run<SyncHostCommand>(["--unknown"], options, TestContext.CancellationToken);
        int noHandler = CliInvoker.Run<SyncHostCommand>([], options, TestContext.CancellationToken);
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("explicit").SetHandler(static (_, _) => 23));
        int explicitCompleted = CliInvoker.Run(explicitModel, [], options, TestContext.CancellationToken);

        Assert.AreEqual(23, completed);
        Assert.AreEqual(23, explicitCompleted);
        Assert.AreEqual(0, help);
        Assert.IsTrue(output.ToString().Contains("custom help", StringComparison.Ordinal));
        Assert.AreEqual(7, invalid);
        Assert.AreEqual(8, noHandler);
        Assert.HasCount(2, errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public async Task RunAsync_MapsPreCancellationToConfiguredExitCode()
    {
        CliHostOptions options = new(cancelledExitCode: 9);
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        int cancelled = await CliInvoker.RunAsync<AsyncHostCommand>(
            ["async"],
            options,
            cancellationSource.Token).ConfigureAwait(false);

        Assert.AreEqual(9, cancelled);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public async Task RunAsync_UsesExplicitModelHostOutputAndExitCodes()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors, helpRenderer: static (_, _) => "model help", invalidInputExitCode: 7);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .SetHandler(static (_, _) => 23), includeStandardHelp: true);

        int completed = await CliInvoker.RunAsync(model, [], options, TestContext.CancellationToken).ConfigureAwait(false);
        int help = await CliInvoker.RunAsync(model, ["--help"], options, TestContext.CancellationToken).ConfigureAwait(false);
        int invalid = await CliInvoker.RunAsync(model, ["--unknown"], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(23, completed);
        Assert.AreEqual(0, help);
        Assert.AreEqual(7, invalid);
        Assert.IsTrue(output.ToString().Contains("model help", StringComparison.Ordinal));
        Assert.AreNotEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public async Task RunAsync_RegistersDirectivesAndUsesInvocationWriters()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors);
        List<string> calls = [];
        DirectiveBuilder first = new DirectiveBuilder("first")
            .SetHandler((_, _) => calls.Add("first"));
        DirectiveBuilder second = new DirectiveBuilder("second")
            .SetHandler((_, _) => calls.Add("second"));

        int completed = await CliInvoker.RunAsync<SyncHostCommand>(
            ["[first]", "[second]", "sync"], [first, second], options, TestContext.CancellationToken).ConfigureAwait(false);
        int completion = await CliInvoker.RunAsync<SyncHostCommand>(
            ["[completion:query-v1]", "--", "bash", "host sy", "host sy"],
            [PredefinedDirectives.CreateCompletion("host")], options, TestContext.CancellationToken).ConfigureAwait(false);
        int invalid = await CliInvoker.RunAsync<SyncHostCommand>(
            ["[completion:query-v1]", "--", "bash", "invalid", "host sy"],
            [PredefinedDirectives.CreateCompletion("host")], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(23, completed);
        Assert.AreSequenceEqual(["first", "second"], calls);
        Assert.AreEqual(0, completion);
        Assert.AreEqual($"xc3luYw=={Environment.NewLine}", output.ToString());
        Assert.AreEqual(2, invalid);
        Assert.IsTrue(errors.ToString().Contains("Invalid completion", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-005")]
    public void Run_RejectsAsynchronousOrDuplicateAdditionalDirectives()
    {
        int calls = 0;
        DirectiveBuilder sync = new DirectiveBuilder("sync-directive")
            .SetHandler((_, _) => calls++);
        int exitCode = CliInvoker.Run<SyncHostCommand>(["[sync-directive]", "sync"], [sync], cancellationToken: TestContext.CancellationToken);
        Assert.AreEqual(23, exitCode);
        Assert.AreEqual(1, calls);
        Assert.ThrowsExactly<ArgumentException>(() => CliInvoker.Run<SyncHostCommand>(
            ["[debug]", "sync"], [PredefinedDirectives.CreateDebug()], cancellationToken: TestContext.CancellationToken));
        Assert.ThrowsExactly<ArgumentException>(() => CliInvoker.Run<SyncHostCommand>(
            [], [new DirectiveBuilder("duplicate").SetHandler(static (_, _) => 0),
                new DirectiveBuilder("duplicate").SetHandler(static (_, _) => 0)], cancellationToken: TestContext.CancellationToken));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-002")]
    [TestProperty(Metadata.Requirement, "FEAT-15-003")]
    public async Task InvokeAsync_RunsDirectivesInOrderBeforeOrdinaryInvalidOutcome()
    {
        List<string> calls = [];
        DirectiveValueDefinition text = new("text", typeof(string));
        DirectiveValueDefinition count = new("count", typeof(int));
        DirectiveBuilder report = new DirectiveBuilder("report")
            .AddValue(text)
            .AddValue(count)
            .SetHandler((context, _) =>
            {
                Assert.IsTrue(context.TryGetValue(text, out string? value));
                Assert.IsTrue(context.TryGetValue(count, out int number));
                calls.Add($"{value}:{number}");
                return DirectiveResult.ContinueProcessing;
            });
        DirectiveBuilder stop = new DirectiveBuilder("stop")
            .SetHandler((_, _) =>
            {
                calls.Add("stop");
                return DirectiveResult.StopProcessing(0);
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(report)
            .AddDirective(stop)
            .AddOption(new OptionDefinition("--quiet", typeof(bool), OptionArity.BooleanFlag))
            .AddChild(new CommandBuilder("copy")
                .SetHandler((_, _) =>
                {
                    calls.Add("command");
                    return 17;
                })));

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(
                model,
                ["--quiet", "[report:hello world", "1]", "[report:again", "2]", "[stop]", "copy", "--bad"],
                TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreSequenceEqual(["hello world:1", "again:2", "stop"], calls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-002")]
    [TestProperty(Metadata.Requirement, "FEAT-15-003")]
    public async Task InvokeAsync_ValidatesAllDirectivesBeforeRunningAny()
    {
        int calls = 0;
        DirectiveBuilder known = new DirectiveBuilder("known")
            .SetHandler((_, _) =>
            {
                calls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddDirective(known));

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["[known]", "[unknown]"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, result.Outcome);
        Assert.IsFalse(result.ParseResult!.DirectiveInputValid);
        Assert.IsEmpty(result.ParseResult.Directives);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-002")]
    public async Task InvokeAsync_DoesNotRecognizeDirectivesAfterTheFirstCommand()
    {
        int directiveCalls = 0;
        int handlerCalls = 0;
        DirectiveBuilder debug = new DirectiveBuilder("debug")
            .SetHandler((_, _) =>
            {
                directiveCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(debug)
            .AddChild(new CommandBuilder("copy")
                .SetHandler((_, _) =>
                {
                    handlerCalls++;
                    return 0;
                })));

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["copy", "[debug]"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, result.Outcome);
        Assert.AreEqual(0, directiveCalls);
        Assert.AreEqual(0, handlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-005")]
    public void Run_PassesExecutableExamplesToDefaultHelpRenderer()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("declared")
            .AddChild(new CommandBuilder("add").AddExample("$0 add --text AAA")),
            includeStandardHelp: true);
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors, executableName: "actual-binary");

        int synchronous = CliInvoker.Run(model, ["add", "--help"], options, TestContext.CancellationToken);
        StringAssert.Contains(output.ToString(), "actual-binary add --text AAA", StringComparison.Ordinal);
        Assert.AreEqual(0, synchronous);
        Assert.AreEqual(Environment.GetCommandLineArgs()[0], new CliHostOptions().ExecutableName);
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-005")]
    public async Task RunAsync_PassesExecutableExamplesToCustomHelpRenderer()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("declared")
            .AddChild(new CommandBuilder("add").AddExample("$0 add --text AAA")),
            includeStandardHelp: true);
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions custom = new(output, errors,
            helpRenderer: static (document, _) => string.Join('|', document.Examples),
            executableName: "custom-binary");
        int asynchronous = await CliInvoker.RunAsync(model, ["add", "--help"], custom, TestContext.CancellationToken)
            .ConfigureAwait(false);
        StringAssert.Contains(output.ToString(), "custom-binary add --text AAA", StringComparison.Ordinal);
        Assert.AreEqual(0, asynchronous);
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    private async Task<InvocationResult> Invoke(CommandBuilder command)
    {
        return await new CliInvoker()
            .InvokeAsync(CommandModel.Create(command), [], TestContext.CancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "The test exercises the generic static root contract.")]
    private sealed class SyncHostCommand : ISynchronousGeneratedCliCommand, IGeneratedCliCommandBuilder
    {
        public static CommandBuilder CreateBuilder() => new CommandBuilder("host")
            .AddChild(new CommandBuilder("sync").SetHandler(static (_, _) => 23));

        public static CommandModel CreateModel(CliLimits? limits = null, bool includeStandardHelp = false)
            => CommandModel.Create(CreateBuilder(), limits, includeStandardHelp);
    }

    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "The test exercises the generic static root contract.")]
    private sealed class AsyncHostCommand : IGeneratedCliCommand
    {
        public static CommandModel CreateModel(CliLimits? limits = null, bool includeStandardHelp = false)
        {
            return CommandModel.Create(
                new CommandBuilder("host")
                    .AddChild(new CommandBuilder("async")
                        .SetTaskHandler(async (_, cancellationToken) =>
                            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false))),
                limits,
                includeStandardHelp);
        }
    }
}
