using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Aot.Tests;

/// <summary>Checks the generated and explicit parser paths used for AOT verification.</summary>
[TestClass]
public sealed class AotAcceptanceTests
{
    public TestContext TestContext { get; set; } = null!;

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
