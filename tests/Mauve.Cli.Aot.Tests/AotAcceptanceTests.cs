using System;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Aot.Tests;

/// <summary>Checks the generated and explicit parser paths used for AOT verification.</summary>
[TestClass]
public sealed class AotAcceptanceTests
{
    /// <summary>Runs identical parser acceptance scenarios against both model paths.</summary>
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-13")]
    [TestProperty(Metadata.Requirement, "FEAT-13-001")]
    [TestProperty(Metadata.Requirement, "FEAT-13-002")]
    [TestProperty(Metadata.Requirement, "FEAT-13-003")]
    [TestProperty(Metadata.Requirement, "FEAT-13-004")]
    public async Task VerifyGeneratedAndExplicitScenarios()
    {
        ConverterRegistry converters = AotScenario.CreateConverters();
        CommandModel generated = AotRootCommand.CreateModel(includeStandardHelp: true);
        ScenarioHandles generatedHandles = AotScenario.CreateGeneratedHandles(
            generated,
            GlobalPack.Option0,
            GlobalPack.Option1,
            NetworkPack.Option0,
            NetworkPack.Option1,
            NetworkPack.Option2,
            NetworkPack.Option3,
            NetworkPack.Option4,
            RetryPack.Option0);
        await AotScenario.VerifyAsync(generated, generatedHandles, converters).ConfigureAwait(false);

        CommandModel explicitModel = AotScenario.CreateExplicitModel(out ScenarioHandles explicitHandles);
        await AotScenario.VerifyAsync(explicitModel, explicitHandles, converters).ConfigureAwait(false);
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
