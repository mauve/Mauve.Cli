using System.Collections.Generic;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSTestMetadata = Mauve.Governance.Testing.MSTest.Metadata;

namespace Mauve.Cli.Tests.Definitions;

[TestClass]
public sealed class OptionPackTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-004")]
    public async Task InvokeAsync_PreservesOmittedAndExplicitEmptyPackMetadata()
    {
        OptionPackBuilder omitted = new();
        OptionPackBuilder empty = new();
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool")
                .AddPack("omitted", omitted)
                .AddPack("empty", empty, string.Empty, string.Empty)
                .SetHandler((context, _) =>
                {
                    OptionPackContext omittedContext = context.GetOptionPack("omitted");
                    OptionPackContext emptyContext = context.GetOptionPack("empty");
                    Assert.IsNull(omittedContext.Title);
                    Assert.IsNull(omittedContext.Description);
                    Assert.AreEqual(string.Empty, emptyContext.Title);
                    Assert.AreEqual(string.Empty, emptyContext.Description);
                    return 0;
                }));

        InvocationResult result = await new CliInvoker().InvokeAsync(model, [], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-004")]
    public async Task InvokeAsync_BindsNestedPackValuesAndGroupsHelpByNearestTitle()
    {
        OptionDefinition endpoint = new("--endpoint", typeof(string), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition retries = new("--retries", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        OptionPackBuilder retryPack = new OptionPackBuilder().AddOption(retries);
        OptionPackBuilder networkPack = new OptionPackBuilder()
            .AddOption(endpoint)
            .AddPack("retry", retryPack, "Retry");
        int handlerResult = 0;
        CommandModel model = CommandModel.Create(
            new CommandBuilder("fetch")
                .AddPack("network", networkPack, "Network")
                .SetHandler((context, _) =>
                {
                    OptionPackContext network = context.GetOptionPack("network");
                    OptionPackContext retry = network.GetNestedPack("retry");
                    Assert.IsTrue(network.TryGetOptionValue(endpoint, out string? address));
                    Assert.IsTrue(retry.TryGetOptionValue(retries, out int count));
                    handlerResult = address!.Length + count;
                    return handlerResult;
                }));

        HelpDocument help = HelpDocument.Create(model);
        InvocationResult result = await new CliInvoker().InvokeAsync(
            model,
            ["--endpoint=https://example.test", "--retries=2"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("Retry", help.OptionGroups[0].Title);
        Assert.AreEqual("Network", help.OptionGroups[1].Title);
        Assert.AreEqual(22, result.ExitCode);
        Assert.AreEqual(result.ExitCode, handlerResult);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-002")]
    public void Create_RejectsPackCyclesAndVisibleAliasCollisions()
    {
        OptionPackBuilder cycle = new();
        _ = cycle.AddPack("self", cycle);
        _ = Assert.Throws<System.ArgumentException>(() =>
            CommandModel.Create(new CommandBuilder("tool").AddPack("cycle", cycle)));

        OptionDefinition first = new("--first", typeof(string), OptionArity.RequiredScalar, aliases: [new OptionAlias("-x")]);
        OptionDefinition second = new("--second", typeof(string), OptionArity.RequiredScalar, aliases: [new OptionAlias("-x")]);
        OptionPackBuilder firstPack = new OptionPackBuilder().AddOption(first);
        OptionPackBuilder secondPack = new OptionPackBuilder().AddOption(second);

        _ = Assert.Throws<System.ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool")
                .AddPack("first", firstPack)
                .AddPack("second", secondPack)));

        _ = CommandModel.Create(new CommandBuilder("empty").AddPack("empty", new OptionPackBuilder()));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-002")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-005")]
    public void Parse_AllowsPackReuseOnDisjointCommandsWithoutSharingValues()
    {
        OptionDefinition value = new("--value", typeof(string), OptionArity.RequiredScalar, isRequired: true);
        OptionPackBuilder sharedPack = new OptionPackBuilder().AddOption(value);
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool")
                .AddChild(new CommandBuilder("first").AddPack("shared", sharedPack))
                .AddChild(new CommandBuilder("second").AddPack("shared", sharedPack)));
        CliParser parser = new();

        ParseResult first = parser.Parse(model, ["first", "--value=one"]);
        ParseResult second = parser.Parse(model, ["second", "--value=two"]);

        Assert.IsTrue(first.TryGetOptionValue(first.SelectedCommand!, value, out string? firstValue));
        Assert.IsTrue(second.TryGetOptionValue(second.SelectedCommand!, value, out string? secondValue));
        Assert.AreEqual("one", firstValue);
        Assert.AreEqual("two", secondValue);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-005")]
    public void Parse_CreatesIndependentCollectionValuesForEachPackParse()
    {
        OptionDefinition items = new(
            "--item",
            typeof(IReadOnlyList<string>),
            OptionArity.RepeatedSingleCollection,
            elementType: typeof(string));
        OptionPackBuilder pack = new OptionPackBuilder().AddOption(items);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddPack("items", pack));
        CliParser parser = new();

        ParseResult first = parser.Parse(model, ["--item=one"]);
        ParseResult second = parser.Parse(model, ["--item=two"]);

        Assert.IsTrue(first.TryGetOptionValue(model.Root, items, out IReadOnlyList<string>? firstItems));
        Assert.IsTrue(second.TryGetOptionValue(model.Root, items, out IReadOnlyList<string>? secondItems));
        Assert.AreEqual("one", firstItems![0]);
        Assert.AreEqual("two", secondItems![0]);
        Assert.AreNotSame(firstItems, secondItems);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-005")]
    public async Task InvokeAsync_KeepsPackValuesIsolatedAcrossConcurrentInvocations()
    {
        OptionDefinition value = new("--value", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        OptionPackBuilder pack = new OptionPackBuilder().AddOption(value);
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool")
                .AddPack("shared", pack)
                .SetTaskHandlerWithExitCode(async (context, _) =>
                {
                    await Task.Yield();
                    Assert.IsTrue(context.GetOptionPack("shared").TryGetOptionValue(value, out int parsed));
                    return parsed;
                }));
        CliInvoker invoker = new();

        Task<InvocationResult> first = invoker.InvokeAsync(model, ["--value=2"], TestContext.CancellationToken).AsTask();
        Task<InvocationResult> second = invoker.InvokeAsync(model, ["--value=5"], TestContext.CancellationToken).AsTask();
        _ = await Task.WhenAll(first, second).ConfigureAwait(false);

        InvocationResult firstResult = await first.ConfigureAwait(false);
        InvocationResult secondResult = await second.ConfigureAwait(false);
        Assert.AreEqual(2, firstResult.ExitCode);
        Assert.AreEqual(5, secondResult.ExitCode);
    }
}
