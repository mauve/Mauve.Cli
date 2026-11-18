using System;
using System.Collections.Generic;
using System.Linq;
using Mauve.Cli.Definitions;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Definitions;

[TestClass]
public sealed class CommandModelTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-001")]
    public void Create_PreservesOrderedHierarchyMetadataAndCanonicalPaths()
    {
        CommandBuilder root = new CommandBuilder("Mauve.Cli")
        {
            Description = "Root command"
        };
        CommandBuilder file = new CommandBuilder("file")
        {
            Description = "File operations"
        };
        CommandBuilder copy = new CommandBuilder("copy")
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
        Assert.AreEqual(2, model.Root.Children.Count);
        Assert.AreEqual("file", model.Root.Children[0].Name);
        Assert.AreEqual("list", model.Root.Children[1].Name);
        Assert.AreEqual("Copy a file", model.Root.Children[0].Children[0].LongDescription);
        CollectionAssert.AreEqual(new[] { "Mauve.Cli", "file", "copy" }, model.Root.Children[0].Children[0].Path.ToArray());
        CollectionAssert.AreEqual(new[] { "cp" }, model.Root.Children[0].Children[0].Aliases.ToArray());

        root.AddChild(new CommandBuilder("later"));
        Assert.AreEqual(2, model.Root.Children.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-001")]
    public void Create_AllowsACommandWithoutChildren()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool"));

        Assert.AreEqual(0, model.Root.Children.Count);
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
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_AllowsSameNamesOnDisjointBranches()
    {
        CommandBuilder distinctBranches = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("first").AddChild(new CommandBuilder("read")))
            .AddChild(new CommandBuilder("second").AddChild(new CommandBuilder("read")));
        Assert.AreEqual(2, CommandModel.Create(distinctBranches).Root.Children.Count);
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
        CommandBuilder root = new CommandBuilder("tool");
        CommandBuilder child = new CommandBuilder("child");
        root.AddChild(child);
        child.AddChild(root);

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(root));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-01")]
    [TestProperty(Metadata.Requirement, "FEAT-01-002")]
    public void Create_RejectsCommandNodesWithMultipleParents()
    {
        CommandBuilder sharedRoot = new CommandBuilder("tool");
        CommandBuilder shared = new CommandBuilder("shared");
        sharedRoot.AddChild(new CommandBuilder("first").AddChild(shared));
        sharedRoot.AddChild(new CommandBuilder("second").AddChild(shared));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(sharedRoot));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-02")]
    [TestProperty(Metadata.Requirement, "FEAT-02-001")]
    public void Create_PreservesOptionAliasesVisibilityAndTypeMetadata()
    {
        OptionDefinition output = new OptionDefinition(
            "--output",
            typeof(string),
            OptionArity.RequiredScalar,
            new[] { new OptionAlias("-o") },
            isHidden: true,
            description: "Output path");
        CommandBuilder root = new CommandBuilder("tool").AddOption(output);

        CommandDefinition definition = CommandModel.Create(root).Root;

        Assert.AreEqual("--output", definition.Options[0].Name);
        Assert.AreEqual(typeof(string), definition.Options[0].ValueType);
        Assert.AreEqual("-o", definition.Options[0].Aliases[0].Name);
        Assert.IsTrue(definition.Options[0].IsHidden);
        Assert.AreEqual("Output path", definition.Options[0].Description);
        Assert.IsTrue(definition.TryGetOption("--output", out OptionDefinition? canonical));
        Assert.IsTrue(definition.TryGetOption("-o", out OptionDefinition? alias));
        Assert.AreSame(canonical, alias);
        Assert.IsFalse(definition.TryGetOption("--missing", out _));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(
            new CommandBuilder("tool").AddOption(new OptionDefinition("-o", typeof(string), OptionArity.RequiredScalar))));
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

        CollectionAssert.AreEqual(
            new[] { "source", "destination" },
            definition.Positionals.Select(static positional => positional.Name).ToArray());
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
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar, new[] { new OptionAlias("-o") }))
            .AddOption(new OptionDefinition("--other", typeof(string), OptionArity.RequiredScalar, new[] { new OptionAlias("-o") }));

        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(invalid));

        CommandBuilder repeatedAlias = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar, new[] { new OptionAlias("-o"), new OptionAlias("-o") }));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(repeatedAlias));

        CommandBuilder distinctCase = new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--Output", typeof(string), OptionArity.RequiredScalar));

        Assert.AreEqual(2, CommandModel.Create(distinctCase).Root.Options.Count);
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
        Assert.AreEqual(1, CommandModel.Create(nullableOption).Root.Options.Count);

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
        Assert.AreEqual(1, CommandModel.Create(validRest).Root.Positionals.Count);

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
}
