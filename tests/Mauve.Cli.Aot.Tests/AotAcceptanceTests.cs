using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Aot.Tests;

/// <summary>Checks the generated and explicit parser paths used for AOT verification.</summary>
[TestClass]
public sealed class AotAcceptanceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-17")]
    [TestProperty(Metadata.Requirement, "FEAT-17-004")]
    public async Task VerifyMulticallDispatch()
    {
        CommandModel generated = AotMulticallRootCommand.CreateModel(includeStandardHelp: true);
        PositionalDefinition name = new("name", typeof(string));
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("multicall-aot").AddAlias("suite-aot")
            .AddChild(new CommandBuilder("copy").AddAlias("cp").AddPositional(name)
                .SetHandler((context, _) => context.TryGetPositionalValue(name, out string? value)
                    ? value!.Length + context.ParseResult.SelectedPath.Count
                    : throw new InvalidOperationException("The multicall name is unavailable."))),
            includeStandardHelp: true);
        CommandModel other = CommandModel.Create(new CommandBuilder("other-aot").SetHandler(static (_, _) => 7));
        foreach (CommandModel model in new[] { generated, explicitModel })
        {
            using StringWriter output = new();
            using StringWriter errors = new();
            IReadOnlyList<CliMulticallRoute> routes = [new("smurf", ["suite-aot", "cp"])];
            Assert.AreEqual(5, await CliInvoker.DispatchMulticallAsync([model, other], routes, ["Ada"],
                new CliHostOptions(output, errors, executableName: "smurf"),
                TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(5, await CliInvoker.DispatchMulticallAsync([model, other], arguments: ["cp", "Ada"],
                options: new CliHostOptions(output, errors, executableName: "suite-aot"),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(7, await CliInvoker.DispatchMulticallAsync([model, other], arguments: [],
                options: new CliHostOptions(output, errors, executableName: "other-aot"),
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false));
            Assert.AreEqual(0, await CliInvoker.DispatchMulticallAsync([model, other], routes, ["--help"],
                new CliHostOptions(output, errors, executableName: "smurf"),
                TestContext.CancellationToken).ConfigureAwait(false));
            StringAssert.Contains(output.ToString(), "Usage: smurf [OPTIONS] <name>", StringComparison.Ordinal);
            Assert.AreEqual(string.Empty, errors.ToString());
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-09")]
    [TestProperty(Metadata.Requirement, "FEAT-09-006")]
    [TestProperty(Metadata.Requirement, "FEAT-09-007")]
    [TestProperty(Metadata.Requirement, "FEAT-09-008")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA1849:Call async methods when in an async method",
        Justification = "Exercises synchronous and asynchronous hosts after native publication.")]
    public async Task VerifyExceptionHosting()
    {
        using StringWriter errors = new();
        CliHostOptions options = new CliHostOptionsBuilder()
            .WithStandardError(errors)
            .WithExecutableName("native-cli")
            .MapException<Exception>(1, "General native failure.")
            .MapException<IOException>(74, "Native file unavailable.")
            .MapException<CliExitCodeException>(75, static exception => exception.SafeMessage)
            .WithExceptionRenderer(static context => context.ExecutableName + ": " + context.Result.SafeMessage)
            .Build();
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("explicit-exception")
            .SetHandler(static (_, _) => throw new FileNotFoundException("Private file context.")));
        Assert.AreEqual(74, CliInvoker.Run(explicitModel, [], options, TestContext.CancellationToken));
        Assert.AreEqual(74, await CliInvoker.RunAsync(explicitModel, [], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(74, CliInvoker.Run<AotExceptionCommand>([], options, TestContext.CancellationToken));
        Assert.AreEqual(75, await CliInvoker.RunAsync<AotExceptionCommand>(["--direct"], options, TestContext.CancellationToken).ConfigureAwait(false));
        Assert.AreEqual(string.Concat(Enumerable.Repeat("native-cli: Native file unavailable." + Environment.NewLine, 3))
            + "native-cli: Direct native failure." + Environment.NewLine, errors.ToString());
    }

    /// <summary>Exercises generated and explicit validation after native publication.</summary>
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-13")]
    [TestProperty(Metadata.Requirement, "FEAT-13-003")]
    public async Task VerifyGeneratedAndExplicitValidation()
    {
        CommandModel generated = AotValidationCommand.CreateModel();
        OptionDefinition tags = new("tags", typeof(IReadOnlyList<string>),
            OptionArity.RepeatedSingleCollection, elementType: typeof(string), isRequired: true,
            validators:
            [
                CliValidators.Each<string>(CliValidators.StringLength(2, 8)),
                CliValidators.Each<string>(CliValidators.Provider<string, AotTagValidator>())
            ]);
        OptionDefinition count = new("count", typeof(int), OptionArity.RequiredScalar,
            defaultValuePolicy: DefaultValuePolicy.Constant, defaultValue: 3,
            validators: [CliValidators.Range(1, 10)]);
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("explicit")
            .AddOption(tags).AddOption(count).SetHandler(static (_, _) => 4));
        foreach (CommandModel model in new[] { generated, explicitModel })
        {
            InvocationResult valid = await new CliInvoker().InvokeAsync(model, ["--tags=ok"], TestContext.CancellationToken).ConfigureAwait(false);
            ParseResult invalid = new CliParser().Parse(model, ["--tags=ok", "--tags", "bad"]);
            Assert.AreEqual(InvocationOutcome.Completed, valid.Outcome);
            Assert.AreEqual(4, valid.ExitCode);
            Assert.AreEqual("tag.bad", invalid.Diagnostics[0].FailureCode);
            Assert.AreEqual(2, invalid.Diagnostics[0].ValueTokenIndex);
        }
    }

    /// <summary>Runs identical parser acceptance scenarios against both model paths.</summary>
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-13")]
    [TestProperty(Metadata.Requirement, "FEAT-13-001")]
    [TestProperty(Metadata.Requirement, "FEAT-13-002")]
    [TestProperty(Metadata.Requirement, "FEAT-13-003")]
    [TestProperty(Metadata.Requirement, "FEAT-13-004")]
    public async Task VerifyGeneratedAndExplicitScenarios()
    {
        CommandModel generated = AotRootCommand.CreateModel(includeStandardHelp: true);
        HelpDocument help = HelpDocument.Create(generated, generated.Root.Children[0], "native-todo");
        Assert.AreEqual("Add an AOT scenario item.", help.Description);
        Assert.AreEqual("The item text.", help.Positionals[0].Description);
        Assert.AreSequenceEqual(["aot-todo add --count 2 AAA", "native-todo add --count 2 AAA"], [.. help.Examples]);
        await AotScenario.VerifyAsync(generated, null).ConfigureAwait(false);

        CommandModel defaults = AotConversionCommand.CreateModel();
        Assert.IsTrue(defaults.Root.Options.Single(static option => option.Name == "--choice").IsRequired);
        Assert.AreEqual("classic", defaults.Root.Options.Single(static option => option.Name == "--flavor").DefaultValue);
        StringAssert.Contains(HelpTextRenderer.Render(HelpDocument.Create(defaults)), "default: classic", StringComparison.Ordinal);
        CommandModel explicitModel = AotScenario.CreateExplicitModel(out ScenarioHandles explicitHandles);
        await AotScenario.VerifyAsync(explicitModel, explicitHandles).ConfigureAwait(false);
        await AotConversionScenario.VerifyAsync().ConfigureAwait(false);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-001")]
    [TestProperty(Metadata.Requirement, "FEAT-15-004")]
    [TestProperty(Metadata.Requirement, "FEAT-15-005")]
    public async Task VerifyGeneratedAndExplicitDirectives()
    {
        await VerifyGeneratedDirectivesAsync().ConfigureAwait(false);
        await VerifyExplicitDirectivesAsync().ConfigureAwait(false);
        using StringWriter output = new();
        using StringWriter errors = new();
        int exitCode = await CliInvoker.RunAsync<AotDirectiveRootCommand>(
            ["[completion:query-v1]", "--", "bash", "directive ", "directive "],
            [PredefinedDirectives.CreateCompletion("directive")],
            new CliHostOptions(output, errors), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, exitCode, errors.ToString());
        StringAssert.Contains(output.ToString(), "xW3ZlcnNpb25d", StringComparison.Ordinal);
        Assert.AreEqual(string.Empty, errors.ToString());
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-004")]
    public async Task VerifyGeneratedAndExplicitEnvironmentBinding()
    {
        CliHostOptions options = new(
            enableEnvironmentVariables: true,
            environmentVariableLookup: static name => name == "AOT_NAME" ? "native" : null);
        CommandModel generated = AotEnvironmentCommand.CreateModel();
        InvocationResult generatedResult = await new CliInvoker().InvokeAsync(
            generated, [], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, generatedResult.Outcome);
        Assert.AreEqual(6, generatedResult.ExitCode);
        Assert.AreEqual(ValueOrigin.Environment, generatedResult.ParseResult!.Values[0].Origin);

        OptionDefinition name = new(
            "--name",
            typeof(string),
            OptionArity.RequiredScalar,
            isRequired: true,
            environmentVariableName: "AOT_NAME");
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("explicit-environment")
            .AddOption(name)
            .SetHandler((context, _) => context.TryGetOptionValue(name, out string? value)
                ? value!.Length
                : throw new InvalidOperationException("The explicit environment value is unavailable.")));
        InvocationResult explicitResult = await new CliInvoker().InvokeAsync(
            explicitModel, [], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, explicitResult.Outcome);
        Assert.AreEqual(generatedResult.ExitCode, explicitResult.ExitCode);
        Assert.AreEqual(ValueOrigin.Environment, explicitResult.ParseResult!.Values[0].Origin);
    }

    private static async Task VerifyGeneratedDirectivesAsync()
    {
        AotDirectiveState.Reset();
        CommandModel model = AotDirectiveRootCommand.CreateModel();
        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["[debug:typed]", "[version]"])
            .ConfigureAwait(false);

        Assert.AreEqual(
            InvocationOutcome.NoHandler,
            result.Outcome,
            string.Join(" | ", result.ParseResult!.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.IsTrue(AotDirectiveState.IsComplete);
        Assert.AreEqual("typed", AotDirectiveState.CapturedText);
    }

    private static async Task VerifyExplicitDirectivesAsync()
    {
        AotDirectiveState.Reset();
        DirectiveValueDefinition text = new("text", typeof(string), isRequired: true);
        DirectiveBuilder debug = new DirectiveBuilder("debug")
            .AddValue(text)
            .SetHandler((context, _) =>
            {
                if (!context.TryGetValue(text, out string? value))
                {
                    throw new InvalidOperationException("The explicit directive value is unavailable.");
                }

                AotDirectiveState.RecordDebug(value!);
                return DirectiveResult.ContinueProcessing;
            });
        DirectiveBuilder version = new DirectiveBuilder("version")
            .SetHandler(static (_, _) =>
            {
                AotDirectiveState.RecordVersion();
                return DirectiveResult.ContinueProcessing;
            });
        CommandModel model = CommandModel.Create(new CommandBuilder("explicit")
            .AddDirective(debug)
            .AddDirective(version));
        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["[debug:typed]", "[version]"])
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.NoHandler, result.Outcome);
        Assert.IsTrue(AotDirectiveState.IsComplete);
        Assert.AreEqual("typed", AotDirectiveState.CapturedText);
    }
}
