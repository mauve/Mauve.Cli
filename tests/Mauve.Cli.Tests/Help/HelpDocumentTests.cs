using System;
using System.Linq;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Help;

[TestClass]
public sealed class HelpDocumentTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-001")]
    public void Create_ProjectsVisibleCommandsPositionalsAndOptionAliases()
    {
        CommandBuilder copy = new("copy")
        {
            Description = "Copy files"
        };
        _ = copy.AddAlias("cp");
        _ = copy.AddPositional(new PositionalDefinition("source", typeof(string), description: "Input file"));
        _ = copy.AddPositional(new PositionalDefinition(
            "destination",
            typeof(string),
            minimumValues: 0,
            description: "Output file"));
        _ = copy.AddOption(new OptionDefinition(
            "--count",
            typeof(int),
            OptionArity.RequiredScalar,
            isRequired: true));
        _ = copy.AddOption(new OptionDefinition(
            "--format",
            typeof(string),
            OptionArity.OptionalScalar,
            aliases: [new OptionAlias("-f")],
            bareValuePolicy: BareValuePolicy.Null,
            allowsNull: true,
            valueLabel: "kind"));

        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(copy)
            .AddChild(new CommandBuilder("hidden") { IsHidden = true });
        CommandModel model = CommandModel.Create(root);

        HelpDocument rootHelp = HelpDocument.Create(model);
        HelpDocument leafHelp = HelpDocument.Create(model, model.Root.Children[0]);
        string renderedLeaf = HelpTextRenderer.Render(leafHelp);

        Assert.AreEqual("tool", rootHelp.Usage);
        Assert.AreEqual(1, rootHelp.Children.Count);
        Assert.AreEqual("copy", rootHelp.Children[0].Name);
        CollectionAssert.AreEqual(new[] { "cp" }, rootHelp.Children[0].Aliases.ToArray());
        Assert.AreEqual("tool copy [OPTIONS] <source> [destination]", leafHelp.Usage);
        Assert.AreEqual("@ungrouped", leafHelp.OptionGroups[0].Key);
        Assert.IsTrue(renderedLeaf.Contains("--count", StringComparison.Ordinal));
        Assert.IsTrue(renderedLeaf.Contains("required", StringComparison.Ordinal));
        Assert.AreEqual(
            "--format, -f [=kind]",
            leafHelp.OptionGroups.SelectMany(static group => group.Options)
                .Single(static option => option.Syntax.StartsWith("--format", StringComparison.Ordinal))
                .Syntax);
        Assert.IsTrue(renderedLeaf.Contains("Optional values must use '='.", StringComparison.Ordinal));
        Assert.IsFalse(renderedLeaf.Contains("hidden", StringComparison.Ordinal));
        Assert.ThrowsExactly<ArgumentException>(() =>
            HelpDocument.Create(model, model.Root.Children[1]));

        HelpDocument empty = HelpDocument.Create(CommandModel.Create(new CommandBuilder("empty")));
        Assert.AreEqual("empty", empty.Usage);
        Assert.AreEqual("Usage: empty\n", HelpTextRenderer.Render(empty));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-12")]
    [TestProperty(Metadata.Requirement, "FEAT-12-004")]
    public void Create_ProjectsInheritedOptionsWithTheirDeclaringOwner()
    {
        OptionDefinition inherited = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            scope: OptionScope.Inherited,
            displayGroup: new OptionDisplayGroup("shared", "Global"));
        CommandBuilder leaf = new CommandBuilder("copy")
            .AddOption(new OptionDefinition(
                "--force",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("shared", "Local")));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(inherited)
            .AddChild(leaf));
        HelpDocument help = HelpDocument.Create(model, model.Root.Children[0]);

        Assert.AreEqual(2, help.OptionGroups.Count);
        Assert.AreEqual("Local", help.OptionGroups[0].Title);
        Assert.AreEqual("Global", help.OptionGroups[1].Title);
        CollectionAssert.AreEqual(new[] { "tool" }, help.OptionGroups[1].OwnerPath.ToArray());
        Assert.AreEqual(
            "--verbose",
            help.OptionGroups.SelectMany(static group => group.Options)
                .Single(static option => option.Syntax.StartsWith("--verbose", StringComparison.Ordinal))
                .Syntax);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-002")]
    public void Create_SanitizesMetadataAndSuppressesHiddenOrSensitiveDefaults()
    {
        int factoryCalls = 0;
        CommandBuilder root = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--safe",
                typeof(string),
                OptionArity.RequiredScalar,
                description: "safe\u001b[31m text\u202e\nnext",
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    throw new InvalidOperationException("must not run");
                },
                defaultDisplay: "static default"))
            .AddOption(new OptionDefinition(
                "--secret",
                typeof(string),
                OptionArity.RequiredScalar,
                isSensitive: true,
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () => "secret",
                defaultDisplay: "secret display"))
            .AddOption(new OptionDefinition(
                "--inert",
                typeof(string),
                OptionArity.RequiredScalar,
                isRequired: true,
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () => "inert",
                defaultDisplay: "inert display"))
            .AddOption(new OptionDefinition(
                "--hidden",
                typeof(string),
                OptionArity.RequiredScalar,
                isHidden: true,
                description: "hidden text"));
        CommandModel model = CommandModel.Create(root);

        HelpDocument document = HelpDocument.Create(model);
        string rendered = HelpTextRenderer.Render(document);

        Assert.AreEqual(0, factoryCalls);
        Assert.IsTrue(document.OptionGroups[0].Options[0].Description.Contains('\n', StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains('\u001b', StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains('\u202e', StringComparison.Ordinal));
        Assert.IsTrue(rendered.Contains("--secret", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("secret display", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("inert display", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("--hidden", StringComparison.Ordinal));
        Assert.IsTrue(rendered.Contains("static default", StringComparison.Ordinal));
        Assert.IsTrue(rendered.Contains("safe[31m text", StringComparison.Ordinal));

        string customOutput = string.Empty;
        HelpOutput.Write(
            document,
            text => customOutput = text,
            renderer: (help, _) => string.Join(
                "|",
                help.OptionGroups.SelectMany(static group => group.Options)
                    .Select(static option => string.Concat(option.Description, option.DefaultDisplay))));
        Assert.IsFalse(customOutput.Contains("secret display", StringComparison.Ordinal));
        Assert.IsFalse(customOutput.Contains('\u001b', StringComparison.Ordinal));
        Assert.IsTrue(customOutput.Contains("static default", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-003")]
    public void Write_PreservesGroupOrderAndUsesCustomRendererAndSink()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--first",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("first", "First group", "First description")))
            .AddOption(new OptionDefinition(
                "--first-more",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("first", "First group", "First description")))
            .AddOption(new OptionDefinition(
                "--second",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("second", "Second group")));
        HelpDocument document = HelpDocument.Create(CommandModel.Create(root));
        string[] written = [];

        HelpOutput.Write(
            document,
            text => written = [text],
            48,
            (received, width) => $"{received.OptionGroups[0].Options[0].Syntax};width={width};groups={received.OptionGroups.Count}");

        Assert.AreEqual("--first;width=48;groups=2", written[0]);
        Assert.AreEqual("first", document.OptionGroups[0].Key);
        Assert.AreEqual("second", document.OptionGroups[1].Key);
        Assert.AreEqual(2, document.OptionGroups[0].Options.Count);
        CollectionAssert.AreEqual(new[] { "tool" }, document.OptionGroups[0].OwnerPath.ToArray());
        string rendered = HelpTextRenderer.Render(document, 48);
        Assert.IsTrue(rendered.Split('\n').All(static line => line.Length <= 48));
        Assert.IsTrue(rendered.IndexOf("First group:", StringComparison.Ordinal)
            < rendered.IndexOf("Second group:", StringComparison.Ordinal));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            HelpOutput.Write(document, static _ => throw new InvalidOperationException("sink fault")));

        CommandBuilder conflictingGroups = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--first",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("shared", "First title")))
            .AddOption(new OptionDefinition(
                "--second",
                typeof(bool),
                OptionArity.BooleanFlag,
                displayGroup: new OptionDisplayGroup("shared", "Different title")));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(conflictingGroups));
    }
}
