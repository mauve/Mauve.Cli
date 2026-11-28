using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class DirectiveAuditTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-003")]
    public async Task InvokeAsync_StopSuppressesLaterDirectivesAndCommandHandler()
    {
        int laterDirectiveCalls = 0;
        int commandHandlerCalls = 0;
        DirectiveBuilder stop = new DirectiveBuilder("stop")
            .SetHandler(static (_, _) => DirectiveResult.StopProcessing(27));
        DirectiveBuilder later = new DirectiveBuilder("later")
            .SetHandler((_, _) =>
            {
                laterDirectiveCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(stop)
            .AddDirective(later)
            .SetHandler((_, _) =>
            {
                commandHandlerCalls++;
                return 0;
            }));

        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["[stop]", "[later]"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(27, result.ExitCode);
        Assert.AreEqual(0, laterDirectiveCalls);
        Assert.AreEqual(0, commandHandlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-003")]
    public async Task InvokeAsync_CancellationSuppressesLaterDirectivesAndCommandHandler()
    {
        int laterDirectiveCalls = 0;
        int commandHandlerCalls = 0;
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        DirectiveBuilder wait = new DirectiveBuilder("wait")
            .SetTaskHandler(async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return DirectiveResult.ContinueProcessing;
            });
        DirectiveBuilder later = new DirectiveBuilder("later")
            .SetHandler((_, _) =>
            {
                laterDirectiveCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(wait)
            .AddDirective(later)
            .SetHandler((_, _) =>
            {
                commandHandlerCalls++;
                return 0;
            }));

        ValueTask<InvocationResult> pending = new CliInvoker()
            .InvokeAsync(model, ["[wait]", "[later]"], cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        InvocationResult result = await pending.ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, laterDirectiveCalls);
        Assert.AreEqual(0, commandHandlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-003")]
    public async Task InvokeAsync_DirectiveFaultPropagatesWithoutRunningLaterCallbacks()
    {
        InvalidOperationException expected = new("directive failure");
        int laterDirectiveCalls = 0;
        int commandHandlerCalls = 0;
        DirectiveBuilder fault = new DirectiveBuilder("fault")
            .SetTaskHandler((_, _) => Task.FromException<DirectiveResult>(expected));
        DirectiveBuilder later = new DirectiveBuilder("later")
            .SetHandler((_, _) =>
            {
                laterDirectiveCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(fault)
            .AddDirective(later)
            .SetHandler((_, _) =>
            {
                commandHandlerCalls++;
                return 0;
            }));

        InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new CliInvoker()
                .InvokeAsync(model, ["[fault]", "[later]"], TestContext.CancellationToken)
                .ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(0, laterDirectiveCalls);
        Assert.AreEqual(0, commandHandlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-002")]
    public async Task InvokeAsync_MissingExcessAndUnconvertibleValuesDiscardEarlierValidDirective()
    {
        (string Name, string[] Arguments)[] cases =
        [
            ("missing", ["[earlier]", "[missing]"]),
            ("excess", ["[earlier]", "[excess:first", "second]"]),
            ("unconvertible", ["[earlier]", "[unconvertible:not-an-integer]"])
        ];

        foreach ((string name, string[] arguments) in cases)
        {
            int directiveCalls = 0;
            int commandHandlerCalls = 0;
            DirectiveBuilder earlier = new DirectiveBuilder("earlier")
                .SetHandler((_, _) =>
                {
                    directiveCalls++;
                    return DirectiveResult.ContinueProcessing;
                });
            DirectiveBuilder invalid = name switch
            {
                "missing" => new DirectiveBuilder("missing")
                    .AddValue(new DirectiveValueDefinition("required", typeof(string)))
                    .SetHandler((_, _) => DirectiveResult.ContinueProcessing),
                "excess" => new DirectiveBuilder("excess")
                    .AddValue(new DirectiveValueDefinition("single", typeof(string)))
                    .SetHandler((_, _) => DirectiveResult.ContinueProcessing),
                "unconvertible" => new DirectiveBuilder("unconvertible")
                    .AddValue(new DirectiveValueDefinition("number", typeof(int)))
                    .SetHandler((_, _) => DirectiveResult.ContinueProcessing),
                _ => throw new InvalidOperationException("Unknown directive input case.")
            };
            CommandModel model = CommandModel.Create(new CommandBuilder("tool")
                .AddDirective(earlier)
                .AddDirective(invalid)
                .SetHandler((_, _) =>
                {
                    commandHandlerCalls++;
                    return 0;
                }));

            InvocationResult result = await new CliInvoker()
                .InvokeAsync(model, arguments, TestContext.CancellationToken)
                .ConfigureAwait(false);

            Assert.AreEqual(InvocationOutcome.InvalidInput, result.Outcome, name);
            Assert.IsFalse(result.ParseResult!.DirectiveInputValid, name);
            Assert.IsEmpty(result.ParseResult.Directives, name);
            Assert.AreEqual(0, directiveCalls, name);
            Assert.AreEqual(0, commandHandlerCalls, name);
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-005")]
    public async Task ProjectDirectives_HidesHiddenAndSensitiveMetadataWithoutInvokingCallbacks()
    {
        int callbackCalls = 0;
        DirectiveBuilder visible = new DirectiveBuilder("visible")
            .AddValue(new DirectiveValueDefinition("format", typeof(ProjectionChoice)))
            .AddValue(new DirectiveValueDefinition("hidden-value-name", typeof(ProjectionChoice), isHidden: true))
            .AddValue(new DirectiveValueDefinition("sensitive-value-name", typeof(ProjectionChoice), isSensitive: true))
            .SetHandler((_, _) =>
            {
                callbackCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        DirectiveBuilder hidden = new("concealed")
        {
            IsHidden = true
        };
        _ = hidden.SetHandler((_, _) =>
        {
            callbackCalls++;
            return DirectiveResult.ContinueProcessing;
        });
        DirectiveBuilder hiddenValue = new DirectiveBuilder("hiddenvalue")
            .AddValue(new DirectiveValueDefinition("value", typeof(ProjectionChoice), isHidden: true))
            .SetHandler((_, _) =>
            {
                callbackCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        DirectiveBuilder sensitiveValue = new DirectiveBuilder("sensitivevalue")
            .AddValue(new DirectiveValueDefinition("value", typeof(ProjectionChoice), isSensitive: true))
            .SetHandler((_, _) =>
            {
                callbackCalls++;
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(visible)
            .AddDirective(hidden)
            .AddDirective(hiddenValue)
            .AddDirective(sensitiveValue));
        HelpDocument help = HelpDocument.Create(model, model.Root);
        string renderedHelp = HelpTextRenderer.Render(help);
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum([
                new KeyValuePair<string, ProjectionChoice>("plain", ProjectionChoice.Plain),
                new KeyValuePair<string, ProjectionChoice>("pretty", ProjectionChoice.Pretty),
                new KeyValuePair<string, ProjectionChoice>("hidden-choice", ProjectionChoice.Hidden),
                new KeyValuePair<string, ProjectionChoice>("sensitive-choice", ProjectionChoice.Sensitive)
            ])
            .Build();
        CompletionService completion = new(converters);
        CompletionResult visibleValue = await completion.CompleteAsync(
            new CompletionRequest(model, ["[visible:pr"], 0, "[visible:pr".Length),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult hiddenName = await completion.CompleteAsync(
            new CompletionRequest(model, ["[concealed"], 0, "[concealed".Length),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult hiddenValueCandidates = await completion.CompleteAsync(
            new CompletionRequest(model, ["[hiddenvalue:h"], 0, "[hiddenvalue:h".Length),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult sensitiveValueCandidates = await completion.CompleteAsync(
            new CompletionRequest(model, ["[sensitivevalue:s"], 0, "[sensitivevalue:s".Length),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        string visibleSyntax = help.Directives
            .Single(static item => item.Syntax.StartsWith("[visible", StringComparison.Ordinal))
            .Syntax;
        Assert.AreEqual("[visible <format>]", visibleSyntax);
        Assert.IsFalse(renderedHelp.Contains("concealed", StringComparison.Ordinal));
        Assert.IsFalse(renderedHelp.Contains("hidden-value-name", StringComparison.Ordinal));
        Assert.IsFalse(renderedHelp.Contains("sensitive-value-name", StringComparison.Ordinal));
        Assert.IsFalse(renderedHelp.Contains("hidden-choice", StringComparison.Ordinal));
        Assert.IsFalse(renderedHelp.Contains("sensitive-choice", StringComparison.Ordinal));
        Assert.AreEqual("[visible:pretty]", visibleValue.Candidates.Single().InsertionText);
        Assert.IsEmpty(hiddenName.Candidates);
        Assert.IsEmpty(hiddenValueCandidates.Candidates);
        Assert.IsEmpty(sensitiveValueCandidates.Candidates);
        Assert.AreEqual(0, callbackCalls);
    }

    private enum ProjectionChoice
    {
        Plain,
        Pretty,
        Hidden,
        Sensitive
    }
}
