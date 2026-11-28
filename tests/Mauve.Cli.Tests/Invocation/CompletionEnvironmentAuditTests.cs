using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Declarations;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class CompletionEnvironmentAuditTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task CompletionRegistration_DoesNotReadEnvironmentForExplicitOrGeneratedRoots()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int lookups = 0;
        int handlers = 0;
        CliHostOptions options = new(output, errors, enableEnvironmentVariables: true,
            environmentVariableLookup: _ =>
            {
                lookups++;
                throw new InvalidOperationException("Completion must not query the environment.");
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(string), OptionArity.RequiredScalar))
            .AddDirective(PredefinedDirectives.CreateCompletion("tool"))
            .SetHandler((_, _) => handlers++));

        foreach (string shell in new[] { "bash", "zsh", "fish", "powershell" })
        {
            int exit = await CliInvoker.RunAsync(model, [$"[completion:{shell}]"], options,
                TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(0, exit);
            Assert.Contains("query-v1", output.ToString(), StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
        }

        int generatedExit = await CliInvoker.RunAsync<CompletionEnvironmentGeneratedCommand>(
            ["[completion:bash]"], [PredefinedDirectives.CreateCompletion("generated-tool")],
            options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, generatedExit);
        Assert.Contains("query-v1", output.ToString(), StringComparison.Ordinal);
        Assert.AreEqual(string.Empty, errors.ToString());
        Assert.AreEqual(0, lookups);
        Assert.AreEqual(0, handlers);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task CompletionQuery_DoesNotReadEnvironmentWhenPayloadIsStructurallyValid()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int lookups = 0;
        int handlers = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(string), OptionArity.RequiredScalar))
            .AddPositional(new PositionalDefinition("payload", typeof(IReadOnlyList<string>),
                minimumValues: 0, maximumValues: null, elementType: typeof(string)))
            .AddDirective(PredefinedDirectives.CreateCompletion("tool"))
            .AddChild(new CommandBuilder("add").SetHandler((_, _) => handlers++)));
        string[] arguments = ["[completion:query-v1]", "--", "bash", "tool ad", "tool ad"];
        Assert.AreEqual(ParseOutcome.Validated, new CliParser().Parse(model, arguments).Outcome);
        CliHostOptions options = new(output, errors, enableEnvironmentVariables: true,
            environmentVariableLookup: _ =>
            {
                lookups++;
                throw new InvalidOperationException("Completion must not query the environment.");
            });

        int exit = await CliInvoker.RunAsync(model, arguments, options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, exit);
        Assert.AreEqual($"xYWRk{Environment.NewLine}", output.ToString());
        Assert.AreEqual(string.Empty, errors.ToString());
        Assert.AreEqual(0, handlers);
        Assert.AreEqual(0, lookups);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task CustomCompletionNamedDirective_PreservesEnvironmentBindingAndInvocation()
    {
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar);
        DirectiveValueDefinition mode = new("mode", typeof(string));
        int directives = 0;
        int handlers = 0;
        int lookups = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(value)
            .AddDirective(new DirectiveBuilder("completion").AddValue(mode).SetHandler((context, _) =>
            {
                directives++;
                Assert.IsTrue(context.TryGetValue(mode, out string? selectedMode));
                Assert.AreEqual("bash", selectedMode);
                Assert.IsTrue(context.ParseResult.TryGetOptionValue(value, out string? bound));
                Assert.AreEqual("from-environment", bound);
            }))
            .SetHandler((_, _) => handlers++));
        CliHostOptions options = new(enableEnvironmentVariables: true, environmentVariableLookup: _ =>
        {
            lookups++;
            return "from-environment";
        });

        InvocationResult custom = await new CliInvoker().InvokeAsync(
            model, ["[completion:bash]"], options, TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult ordinary = await new CliInvoker().InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, custom.Outcome);
        Assert.AreEqual(InvocationOutcome.Completed, ordinary.Outcome);
        Assert.AreEqual(1, directives);
        Assert.AreEqual(2, handlers);
        Assert.AreEqual(2, lookups);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task Completion_PreservesDirectiveOrderingInvalidHelpAndCancellation()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        int lookups = 0;
        List<string> calls = [];
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(string), OptionArity.RequiredScalar))
            .AddDirective(new DirectiveBuilder("before").SetHandler((_, _) => calls.Add("before")))
            .AddDirective(PredefinedDirectives.CreateCompletion("tool"))
            .AddDirective(new DirectiveBuilder("after").SetHandler((_, _) => calls.Add("after"))),
            includeStandardHelp: true);
        CliHostOptions options = new(output, errors, enableEnvironmentVariables: true,
            environmentVariableLookup: _ => { lookups++; return "value"; });

        InvocationResult completed = await new CliInvoker().InvokeAsync(model,
            ["[before]", "[completion:bash]", "[after]"], options, TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult invalid = await new CliInvoker().InvokeAsync(model,
            ["[before]", "[completion]"], options, TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult help = await new CliInvoker().InvokeAsync(model,
            ["--help"], options, TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult completionWithHelp = await new CliInvoker().InvokeAsync(model,
            ["[completion:bash]", "--help"], options, TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult completionWithInvalid = await new CliInvoker().InvokeAsync(model,
            ["[completion:bash]", "--unknown"], options, TestContext.CancellationToken).ConfigureAwait(false);
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync().ConfigureAwait(false);
        InvocationResult cancellation = await new CliInvoker().InvokeAsync(model,
            ["[completion:bash]"], options, cancelled.Token).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, completed.Outcome);
        Assert.AreEqual(0, completed.ExitCode);
        Assert.AreSequenceEqual(["before"], calls);
        Assert.AreEqual(InvocationOutcome.InvalidInput, invalid.Outcome);
        Assert.AreEqual(InvocationOutcome.HelpRequested, help.Outcome);
        Assert.AreEqual(InvocationOutcome.Completed, completionWithHelp.Outcome);
        Assert.AreEqual(InvocationOutcome.Completed, completionWithInvalid.Outcome);
        Assert.AreEqual(InvocationOutcome.Cancelled, cancellation.Outcome);
        Assert.AreEqual(0, lookups);
        Assert.Contains("query-v1", output.ToString(), StringComparison.Ordinal);
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task Completion_EarlierStoppingDirectiveSeesDefaultInsteadOfEnvironment()
    {
        using StringWriter output = new();
        using StringWriter errors = new();
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: "default");
        List<string?> observed = [];
        int lookups = 0;
        int handlers = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(value)
            .AddDirective(new DirectiveBuilder("stop").SetHandler((context, _) =>
            {
                Assert.IsTrue(context.ParseResult.TryGetOptionValue(value, out string? bound));
                observed.Add(bound);
                return DirectiveResult.StopProcessing(17);
            }))
            .AddDirective(PredefinedDirectives.CreateCompletion("tool"))
            .SetHandler((_, _) => handlers++));
        CliHostOptions options = new(output, errors, enableEnvironmentVariables: true,
            environmentVariableLookup: _ =>
            {
                lookups++;
                return "environment";
            });

        InvocationResult completion = await new CliInvoker().InvokeAsync(model,
            ["[stop]", "[completion:bash]"], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, completion.Outcome);
        Assert.AreEqual(17, completion.ExitCode);
        Assert.AreSequenceEqual(["default"], observed);
        Assert.AreEqual(0, lookups);
        Assert.AreEqual(string.Empty, output.ToString());

        InvocationResult ordinary = await new CliInvoker().InvokeAsync(model,
            ["[stop]"], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, ordinary.Outcome);
        Assert.AreEqual(17, ordinary.ExitCode);
        Assert.AreSequenceEqual(["default", "environment"], observed);
        Assert.AreEqual(1, lookups);
        Assert.AreEqual(0, handlers);
        Assert.AreEqual(string.Empty, errors.ToString());
    }
}

[CliCommand("generated-tool", IsRoot = true)]
public sealed partial class CompletionEnvironmentGeneratedCommand
{
    [CliOption]
    public string? Value { get; init; }

    [CliHandler]
    public int Run() => Value?.Length ?? 0;
}
