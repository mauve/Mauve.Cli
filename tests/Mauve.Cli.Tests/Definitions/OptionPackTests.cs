using System;
using System.Collections.Generic;
using System.Linq;
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
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-001")]
    public async Task InvokeAsync_RejectsHandlesAndNestedKeysOutsidePackView()
    {
        OptionDefinition nestedValue = new("--nested", typeof(int), OptionArity.RequiredScalar);
        OptionDefinition otherValue = new("--other", typeof(int), OptionArity.RequiredScalar);
        OptionPackBuilder parent = new OptionPackBuilder()
            .AddPack("nested", new OptionPackBuilder().AddOption(nestedValue));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPack("parent", parent)
            .AddPack("other", new OptionPackBuilder().AddOption(otherValue))
            .SetHandler((context, _) =>
            {
                OptionPackContext view = context.GetOptionPack("parent");
                Assert.IsTrue(view.TryGetOptionValue(nestedValue, out int value));
                Assert.AreEqual(7, value);
                Assert.ThrowsExactly<ArgumentException>(() => view.TryGetOptionValue(otherValue, out int _));
                Assert.ThrowsExactly<ArgumentException>(() => view.GetNestedPack("other"));
                Assert.ThrowsExactly<ArgumentException>(() => view.GetNestedPack("Nested"));
                Assert.ThrowsExactly<ArgumentException>(() =>
                    view.GetNestedPack("nested").TryGetOptionValue(otherValue, out int _));
                return 0;
            }));

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["--nested=7", "--other=8"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-004")]
    public void Create_InheritsNearestPackTitleAndSanitizesGroupMetadataWithoutMutatingOptions()
    {
        OptionDefinition nested = new("--nested", typeof(string), OptionArity.RequiredScalar, description: "Nested description");
        OptionDefinition parent = new("--parent", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition outer = new("--outer", typeof(string), OptionArity.RequiredScalar);
        OptionPackBuilder parentPack = new OptionPackBuilder()
            .AddOption(parent)
            .AddPack("nested", new OptionPackBuilder().AddOption(nested));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPack("outer", new OptionPackBuilder()
                .AddOption(outer)
                .AddPack("parent", parentPack, "Network\u001b\u202e", "Details\u001b\u202e"), "Outer"));

        HelpDocument help = HelpDocument.Create(model);
        string rendered = HelpTextRenderer.Render(help);

        Assert.HasCount(2, help.OptionGroups);
        Assert.AreEqual("Network", help.OptionGroups[0].Title);
        Assert.AreEqual("Details", help.OptionGroups[0].Description);
        Assert.AreSequenceEqual(
            ["--nested <value>", "--parent <value>"],
            help.OptionGroups[0].Options.Select(static option => option.Syntax));
        Assert.AreEqual("Outer", help.OptionGroups[1].Title);
        Assert.AreEqual("--outer <value>", help.OptionGroups[1].Options.Single().Syntax);
        Assert.IsFalse(rendered.Contains('\u001b', StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains('\u202e', StringComparison.Ordinal));
        Assert.Contains("Network", rendered, StringComparison.Ordinal);
        Assert.Contains("Details", rendered, StringComparison.Ordinal);
        Assert.IsNull(nested.DisplayGroup);
        Assert.AreEqual("Nested description", nested.Description);
        Assert.IsNull(parent.DisplayGroup);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-002")]
    public void Create_RejectsPackCyclesAndVisibleAliasCollisions()
    {
        OptionPackBuilder cycle = new();
        _ = cycle.AddPack("self", cycle);
        _ = Assert.Throws<ArgumentException>(() =>
            CommandModel.Create(new CommandBuilder("tool").AddPack("cycle", cycle)));

        OptionDefinition first = new("--first", typeof(string), OptionArity.RequiredScalar, aliases: [new OptionAlias("-x")]);
        OptionDefinition second = new("--second", typeof(string), OptionArity.RequiredScalar, aliases: [new OptionAlias("-x")]);
        OptionPackBuilder firstPack = new OptionPackBuilder().AddOption(first);
        OptionPackBuilder secondPack = new OptionPackBuilder().AddOption(second);

        _ = Assert.Throws<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool")
                .AddPack("first", firstPack)
                .AddPack("second", secondPack)));

        _ = CommandModel.Create(new CommandBuilder("empty").AddPack("empty", new OptionPackBuilder()));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-002")]
    public void Create_RejectsDuplicateSiblingPackKeysAtEachLevel()
    {
        CommandBuilder duplicateRoot = new CommandBuilder("tool")
            .AddPack("same", new OptionPackBuilder())
            .AddPack("same", new OptionPackBuilder());
        OptionPackBuilder duplicateNested = new OptionPackBuilder()
            .AddPack("same", new OptionPackBuilder())
            .AddPack("same", new OptionPackBuilder());

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(duplicateRoot));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CommandModel.Create(new CommandBuilder("tool").AddPack("parent", duplicateNested)));

        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPack("first", new OptionPackBuilder().AddPack("same", new OptionPackBuilder()))
            .AddPack("second", new OptionPackBuilder().AddPack("same", new OptionPackBuilder())));
        Assert.IsEmpty(model.Root.Options);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-002")]
    public void Create_EnforcesPackDepthAndModelNodeBoundaries()
    {
        OptionPackBuilder nested = new OptionPackBuilder()
            .AddOption(new OptionDefinition("--value", typeof(string), OptionArity.RequiredScalar));
        CommandBuilder root = new CommandBuilder("tool")
            .AddPack("parent", new OptionPackBuilder().AddPack("nested", nested));

        CommandModel model = CommandModel.Create(root, new CliLimits(maximumCommandDepth: 2, maximumModelNodes: 4));
        Assert.HasCount(1, model.Root.Options);
        Assert.ThrowsExactly<ArgumentException>(() =>
            CommandModel.Create(root, new CliLimits(maximumCommandDepth: 1, maximumModelNodes: 4)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            CommandModel.Create(root, new CliLimits(maximumCommandDepth: 2, maximumModelNodes: 3)));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-12")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-12-001")]
    public void Parse_PackOptionsRequireExplicitInheritedScopeOnDescendants()
    {
        OptionDefinition local = new("--local", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition inherited = new("--shared", typeof(string), OptionArity.RequiredScalar, scope: OptionScope.Inherited);
        OptionPackBuilder pack = new OptionPackBuilder().AddOption(local).AddOption(inherited);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPack("pack", pack)
            .AddChild(new CommandBuilder("child")));
        CliParser parser = new();

        ParseResult localAtRoot = parser.Parse(model, ["--local=root"]);
        ParseResult localAtChild = parser.Parse(model, ["child", "--local=child"]);
        ParseResult localBeforeChild = parser.Parse(model, ["--local=root", "child"]);
        ParseResult inheritedAtChild = parser.Parse(model, ["child", "--shared=child"]);

        Assert.AreEqual(ParseOutcome.Validated, localAtRoot.Outcome);
        Assert.IsTrue(localAtRoot.TryGetOptionValue(model.Root, local, out string? rootValue));
        Assert.AreEqual("root", rootValue);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, localAtChild.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.OptionOutOfScope, localBeforeChild.Diagnostics[0].Code);
        Assert.AreEqual(ParseOutcome.Validated, inheritedAtChild.Outcome);
        Assert.IsTrue(inheritedAtChild.TryGetOptionValue(model.Root, inherited, out string? childValue));
        Assert.AreEqual("child", childValue);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-12")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-12-004")]
    public void Create_ExcludesHiddenInheritedPackOptionsFromDescendantHelp()
    {
        OptionDefinition visible = new("--visible", typeof(bool), OptionArity.BooleanFlag, scope: OptionScope.Inherited);
        OptionDefinition hidden = new(
            "--hidden",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: "secret metadata",
            isHidden: true,
            scope: OptionScope.Inherited);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddPack("pack", new OptionPackBuilder().AddOption(visible).AddOption(hidden), "Shared")
            .AddChild(new CommandBuilder("child")));

        HelpDocument help = HelpDocument.Create(model, model.Root.Children[0]);
        string rendered = HelpTextRenderer.Render(help);

        Assert.ContainsSingle(help.OptionGroups.SelectMany(static group => group.Options)
            .Where(static option => option.Syntax == "--visible"));
        Assert.AreSequenceEqual(["tool"], help.OptionGroups.Single().OwnerPath);
        Assert.IsFalse(rendered.Contains("--hidden", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("secret metadata", StringComparison.Ordinal));
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
