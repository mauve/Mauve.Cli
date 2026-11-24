using System;
using System.Collections.Generic;
using System.Linq;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Definitions;

[TestClass]
public sealed class CommandModelTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-003")]
    public void MatchesName_UsesOrdinalCanonicalNamesAndAliases()
    {
        CommandDefinition command = CommandModel.Create(new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy").AddAlias("cp").AddAlias("duplicate"))).Root.Children[0];

        Assert.IsTrue(command.MatchesName("copy"));
        Assert.IsTrue(command.MatchesName("cp"));
        Assert.IsTrue(command.MatchesName("duplicate"));
        Assert.IsFalse(command.MatchesName("COPY"));
        Assert.IsFalse(command.MatchesName("CP"));
        Assert.IsFalse(command.MatchesName("move"));
        Assert.IsFalse(command.MatchesName(string.Empty));
        Assert.ThrowsExactly<ArgumentNullException>(() => command.MatchesName(null!));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-006")]
    public void Create_InstallsHelpTreeAndAccountsForItsLimitsAndCollisions()
    {
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool").AddChild(new CommandBuilder("child")), includeStandardHelp: true);
        Assert.IsTrue(model.Root.TryGetOption("--help-tree", out _));
        Assert.IsTrue(model.Root.Children[0].TryGetOption("--help-tree", out _));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool").AddOption(new OptionDefinition("--help-tree", typeof(bool), OptionArity.BooleanFlag)),
            includeStandardHelp: true));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool"), new CliLimits(maximumOptionsPerCommand: 1), includeStandardHelp: true));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool"), new CliLimits(maximumModelNodes: 2), includeStandardHelp: true));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-001")]
    public void Create_PreservesOrderedHierarchyMetadataAndCanonicalPaths()
    {
        CommandBuilder root = new("Mauve.Cli")
        {
            Description = "Root command"
        };
        CommandBuilder file = new("file")
        {
            Description = "File operations"
        };
        CommandBuilder copy = new("copy")
        {
            LongDescription = "Copy a file"
        };
        copy.AddAlias("cp");
        file.AddChild(copy);
        root.AddChild(file);
        root.AddChild(new CommandBuilder("list"));

        CommandModel model = CommandModel.Create(root);

        Assert.AreEqual("Mauve.Cli", model.Root.Name);
        Assert.AreEqual("Root command", model.Root.Description);
        Assert.HasCount(2, model.Root.Children);
        Assert.AreEqual("file", model.Root.Children[0].Name);
        Assert.AreEqual("list", model.Root.Children[1].Name);
        Assert.AreEqual("Copy a file", model.Root.Children[0].Children[0].LongDescription);
        Assert.AreSequenceEqual(["Mauve.Cli", "file", "copy"], [.. model.Root.Children[0].Children[0].Path]);
        Assert.AreSequenceEqual(["cp"], [.. model.Root.Children[0].Children[0].Aliases]);

        root.AddChild(new CommandBuilder("later"));
        Assert.HasCount(2, model.Root.Children);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-001")]
    public void Create_AllowsACommandWithoutChildren()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));

        Assert.IsEmpty(model.Root.Children);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-001")]
    public void Create_PreservesRootDirectiveOrderAndRejectsDuplicateNames()
    {
        DirectiveBuilder debug = new DirectiveBuilder("debug")
            .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        DirectiveBuilder trace = new DirectiveBuilder("trace")
            .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        CommandBuilder root = new CommandBuilder("tool")
            .AddDirective(debug)
            .AddDirective(trace)
            .AddChild(new CommandBuilder("copy"));

        CommandModel model = CommandModel.Create(root);

        Assert.AreSequenceEqual(["debug", "trace"], model.Directives.Select(static directive => directive.Name));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool")
                .AddDirective(new DirectiveBuilder("debug")
                    .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing))
                .AddDirective(new DirectiveBuilder("debug")
                    .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing))));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-001")]
    public void Create_RejectsDirectiveRegistrationOnChildCommand()
    {
        CommandBuilder child = new CommandBuilder("copy")
            .AddDirective(new DirectiveBuilder("debug")
                .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool").AddChild(child)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_RejectsSiblingNameAndAliasCollisions()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy"))
            .AddChild(new CommandBuilder("move").AddAlias("copy"));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(root));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-12")]
    [TestProperty(Metadata.Requirement, "FEAT-12-003")]
    public void Create_RejectsInheritedOptionCollisionsButAllowsDisjointScopes()
    {
        CommandBuilder collision = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--verbose",
                typeof(bool),
                OptionArity.BooleanFlag,
                scope: OptionScope.Inherited))
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition(
                    "--other",
                    typeof(bool),
                    OptionArity.BooleanFlag,
                    aliases: [new OptionAlias("--verbose")])));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(collision));

        CommandBuilder disjoint = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--format", typeof(string), OptionArity.RequiredScalar))
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition("--format", typeof(string), OptionArity.RequiredScalar)))
            .AddChild(new CommandBuilder("move")
                .AddOption(new OptionDefinition("--format", typeof(string), OptionArity.RequiredScalar)));
        Assert.HasCount(2, CommandModel.Create(disjoint).Root.Children);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_AllowsSameNamesOnDisjointBranches()
    {
        CommandBuilder distinctBranches = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("first").AddChild(new CommandBuilder("read")))
            .AddChild(new CommandBuilder("second").AddChild(new CommandBuilder("read")));
        Assert.HasCount(2, CommandModel.Create(distinctBranches).Root.Children);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_RejectsEmptyOrMalformedCommandNames()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder(string.Empty)));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder("tool").AddChild(new CommandBuilder("bad_name"))));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder("bad root")));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_RejectsCommandCycles()
    {
        CommandBuilder root = new("tool");
        CommandBuilder child = new("child");
        root.AddChild(child);
        child.AddChild(root);

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(root));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_RejectsCommandNodesWithMultipleParents()
    {
        CommandBuilder sharedRoot = new("tool");
        CommandBuilder shared = new("shared");
        sharedRoot.AddChild(new CommandBuilder("first").AddChild(shared));
        sharedRoot.AddChild(new CommandBuilder("second").AddChild(shared));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(sharedRoot));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-001")]
    public void Create_PreservesOptionAliasesVisibilityAndTypeMetadata()
    {
        OptionDefinition output = new(
            "--output",
            typeof(string),
            OptionArity.RequiredScalar,
            [new OptionAlias("-o")],
            isHidden: true,
            description: "Output path");
        CommandBuilder root = new CommandBuilder("tool").AddOption(output);

        CommandDefinition definition = CommandModel.Create(root).Root;

        Assert.AreEqual("--output", definition.Options[0].Name);
        Assert.AreEqual(typeof(string), definition.Options[0].ValueType);
        Assert.AreEqual('o', definition.Options[0].ShortAliases[0]);
        Assert.IsTrue(definition.Options[0].IsHidden);
        Assert.AreEqual("Output path", definition.Options[0].Description);
        Assert.IsTrue(definition.TryGetOption("--output", out OptionDefinition? canonical));
        Assert.IsTrue(definition.TryGetOption("-o", out OptionDefinition? alias));
        Assert.AreSame(canonical, alias);
        Assert.IsFalse(definition.TryGetOption("--missing", out _));
        OptionDefinition shortOnly = new(null, typeof(string), OptionArity.RequiredScalar, shortName: 'o');
        CommandDefinition shortModel = CommandModel.Create(new CommandBuilder("tool").AddOption(shortOnly)).Root;
        Assert.IsTrue(shortModel.TryGetOption("-o", out OptionDefinition? shortOption));
        Assert.AreSame(shortOnly, shortOption);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-001")]
    [TestProperty(Metadata.Requirement, "FEAT-02-002")]
    public void Create_PreservesOmittedAndExplicitEmptyDisplayMetadata()
    {
        CommandBuilder root = new("tool");
        _ = root.AddOption(new OptionDefinition(
            "--omitted",
            typeof(string),
            OptionArity.RequiredScalar));
        _ = root.AddOption(new OptionDefinition(
            "--empty",
            typeof(string),
            OptionArity.RequiredScalar,
            description: string.Empty,
            valueLabel: string.Empty));
        _ = root.AddPositional(new PositionalDefinition("omitted", typeof(string)));
        _ = root.AddPositional(new PositionalDefinition("empty", typeof(string), description: string.Empty));

        CommandDefinition definition = CommandModel.Create(root).Root;

        Assert.IsNull(definition.Description);
        Assert.IsNull(definition.LongDescription);
        Assert.IsNull(definition.Options[0].Description);
        Assert.IsNull(definition.Options[0].ValueLabel);
        Assert.AreEqual(string.Empty, definition.Options[1].Description);
        Assert.AreEqual(string.Empty, definition.Options[1].ValueLabel);
        Assert.IsNull(definition.Positionals[0].Description);
        Assert.AreEqual(string.Empty, definition.Positionals[1].Description);
        Assert.IsNull(new OptionDisplayGroup("omitted", "Title").Description);
        Assert.AreEqual(
            string.Empty,
            new OptionDisplayGroup("empty", "Title", string.Empty).Description);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-002")]
    public void Create_PreservesPositionalDeclarationOrder()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("source", typeof(string)))
            .AddPositional(new PositionalDefinition("destination", typeof(string)));

        CommandDefinition definition = CommandModel.Create(root).Root;

        Assert.AreSequenceEqual(
            ["source", "destination"], [.. definition.Positionals.Select(static positional => positional.Name)]);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-002")]
    public void Create_RejectsDuplicatePositionalNames()
    {
        CommandBuilder duplicateNames = new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("source", typeof(string)))
            .AddPositional(new PositionalDefinition("source", typeof(string)));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(duplicateNames));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-003")]
    public void Create_RejectsDuplicateOptionSpellingsButAllowsCaseDifferences()
    {
        CommandBuilder invalid = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar, [new OptionAlias("-o")]))
            .AddOption(new OptionDefinition("--other", typeof(string), OptionArity.RequiredScalar, [new OptionAlias("-o")]));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalid));

        CommandBuilder repeatedAlias = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar, [new OptionAlias("-o"), new OptionAlias("-o")]));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(repeatedAlias));

        CommandBuilder distinctCase = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--Output", typeof(string), OptionArity.RequiredScalar));

        Assert.HasCount(2, CommandModel.Create(distinctCase).Root.Options);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-004")]
    public void Create_ValidatesArityAndOptionalBarePolicies()
    {
        CommandBuilder invalidOption = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--filter", typeof(string), OptionArity.OptionalScalar));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalidOption));

        CommandBuilder nullableOption = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--filter",
                typeof(string),
                OptionArity.OptionalScalar,
                allowsNull: true,
                bareValuePolicy: BareValuePolicy.Null));
        Assert.HasCount(1, CommandModel.Create(nullableOption).Root.Options);

        CommandBuilder invalidFlag = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--verbose", typeof(string), OptionArity.BooleanFlag));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalidFlag));

        CommandBuilder optionalBeforeRequired = new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("source", typeof(string), minimumValues: 0))
            .AddPositional(new PositionalDefinition("destination", typeof(string)));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(optionalBeforeRequired));

        CommandBuilder invalidRest = new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("paths", typeof(IReadOnlyList<string>), maximumValues: null, elementType: typeof(string)))
            .AddPositional(new PositionalDefinition("after", typeof(string)));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalidRest));
        CommandBuilder validRest = new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("paths", typeof(IReadOnlyList<string>), maximumValues: null, elementType: typeof(string)));
        Assert.HasCount(1, CommandModel.Create(validRest).Root.Positionals);

        CommandBuilder invalidCollection = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--values",
                typeof(IReadOnlyList<int>),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(string)));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalidCollection));

        CommandBuilder mutableArrayCollection = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--values",
                typeof(int[]),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(int)));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(mutableArrayCollection));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Create_EnforcesConfiguredModelLimits()
    {
        CommandBuilder tooDeep = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("one").AddChild(new CommandBuilder("two")));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            tooDeep,
            new CliLimits(maximumCommandDepth: 2)));

        CommandBuilder tooManyOptions = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--one", typeof(string), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--two", typeof(string), OptionArity.RequiredScalar));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            tooManyOptions,
            new CliLimits(maximumOptionsPerCommand: 1)));

        CommandBuilder tooManyNodes = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--one", typeof(string), OptionArity.RequiredScalar));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            tooManyNodes,
            new CliLimits(maximumModelNodes: 1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => GC.KeepAlive(new CliLimits(maximumModelNodes: 0)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-004")]
    public void Create_RejectsStandardHelpOptionCollisions()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--help", typeof(bool), OptionArity.BooleanFlag));

        Assert.ThrowsExactly<ArgumentException>(() =>
            CommandModel.Create(root, includeStandardHelp: true));
    }
}
