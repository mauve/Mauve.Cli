using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Examples.Aot;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Todo.Cli;

namespace Mauve.Cli.Tests.Aot;

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
}
