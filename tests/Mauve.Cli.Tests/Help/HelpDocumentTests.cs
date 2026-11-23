using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Help;

[TestClass]
public sealed class HelpDocumentTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-005")]
    public void Create_ExpandsCanonicalExamplesWithoutReplacingDeclaredCommandNames()
    {
        CommandBuilder add = new CommandBuilder("add")
            .AddAlias("new")
            .AddExample("add --text \"A  A\"")
            .AddExample("todo item add --text AAA")
            .AddExample("$0 item add --text AAA")
            .AddExample("$0 add --text AAA")
            .AddExample("new --text AAA")
            .AddExample("additional --text AAA")
            .AddExample("add --text $00 --path x$0 --literal \"$0\"")
            .AddExample("add --text AAA\u001b\u202e");
        CommandModel model = CommandModel.Create(new CommandBuilder("todo")
            .AddExample("$0 --help")
            .AddChild(new CommandBuilder("item").AddChild(add)));
        CommandDefinition selected = model.Root.Children[0].Children[0];
        HelpDocument help = HelpDocument.Create(model, selected, "runner\u001b\u202e");

        Assert.AreSequenceEqual(
            [
                "todo item add --text \"A  A\"",
                "todo item add --text AAA",
                "runner item add --text AAA",
                "runner item add --text AAA",
                "new --text AAA",
                "additional --text AAA",
                "todo item add --text $00 --path x$0 --literal \"$0\"",
                "todo item add --text AAA"
            ],
            [.. help.Examples]);
        Assert.AreEqual("$0 add --text AAA", selected.Examples[3]);
        Assert.AreEqual("todo --help", HelpDocument.Create(model).Examples[0]);
        Assert.AreEqual("runner --help", HelpDocument.Create(model, executableName: "runner").Examples[0]);
        string output = HelpTextRenderer.Render(help);
        StringAssert.Contains(output, "Examples:\n  todo item add --text \"A  A\"\n", StringComparison.Ordinal);
        Assert.IsTrue(HelpTextRenderer.Render(help, 20).Split('\n').All(static line => line.Length <= 20));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-005")]
    public void Create_SnapshotsExamplesAndRejectsInvalidExampleInputs()
    {
        CommandBuilder builder = new CommandBuilder("tool").AddExample("  $0 --help  ");
        CommandModel model = CommandModel.Create(builder);
        _ = builder.AddExample("$0 --version");

        Assert.AreSequenceEqual(["$0 --help"], [.. model.Root.Examples]);
        Assert.ThrowsExactly<ArgumentNullException>(() => builder.AddExample(null!));
        Assert.ThrowsExactly<ArgumentException>(() => builder.AddExample(" "));
        Assert.ThrowsExactly<ArgumentException>(() => builder.AddExample("one\ntwo"));
        Assert.ThrowsExactly<ArgumentException>(() => builder.AddExample("one\rtwo"));
        Assert.ThrowsExactly<ArgumentException>(() => HelpDocument.Create(model, executableName: ""));
    }

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
        Assert.HasCount(1, rootHelp.Children);
        Assert.AreEqual("copy", rootHelp.Children[0].Name);
        Assert.AreSequenceEqual(["cp"], [.. rootHelp.Children[0].Aliases]);
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
        string renderedEmpty = HelpTextRenderer.Render(empty);
        Assert.AreEqual("Usage: empty\n", renderedEmpty);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-001")]
    public void Create_FormatsOptionSpellingsWithConfiguredPrefixes()
    {
        OptionDefinition output = new(
            "output",
            typeof(string),
            OptionArity.RequiredScalar,
            aliases: [new OptionAlias("out")],
            shortName: 'o',
            shortAliases: ['p']);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(output));

        HelpDocument help = HelpDocument.Create(model, syntax: new CliSyntaxOptions("++", "+"));

        Assert.AreEqual("++output, +o, ++out, +p <value>", help.OptionGroups.Single().Options.Single().Syntax);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-001")]
    public async Task Run_HelpPrintsEnvironmentNamesInlineOnlyWhenEnabled()
    {
        OptionDefinition logLevel = new("--log-level", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition region = new(
            "--region",
            typeof(string),
            OptionArity.RequiredScalar,
            environmentVariableName: "CUSTOM_REGION");
        OptionDefinition disabled = new(
            "--secret",
            typeof(string),
            OptionArity.RequiredScalar,
            environmentVariableBinding: EnvironmentVariableBinding.Disabled);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(logLevel)
            .AddOption(region)
            .AddOption(disabled), includeStandardHelp: true);
        string defaultHostHelp = HelpTextRenderer.Render(HelpDocument.Create(model));
        using StringWriter output = new();
        int lookups = 0;
        CliHostOptions options = new(
            standardOutput: output,
            enableEnvironmentVariables: true,
            environmentVariablePrefix: "MY_APP",
            environmentVariableLookup: _ =>
            {
                lookups++;
                return "private-value";
            });

        int exitCode = await CliInvoker.RunAsync(
            model,
            ["--help"],
            options,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual(0, lookups);
        Assert.IsFalse(defaultHostHelp.Contains("env:", StringComparison.Ordinal));
        StringAssert.Contains(output.ToString(), "--log-level <value>", StringComparison.Ordinal);
        StringAssert.Contains(output.ToString(), "env: MY_APP_LOG_LEVEL", StringComparison.Ordinal);
        StringAssert.Contains(output.ToString(), "env: CUSTOM_REGION", StringComparison.Ordinal);
        Assert.IsFalse(output.ToString().Contains("env: MY_APP_SECRET", StringComparison.Ordinal));
        Assert.IsFalse(output.ToString().Contains("Environment variables:", StringComparison.Ordinal));
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

        Assert.HasCount(2, help.OptionGroups);
        Assert.AreEqual("Local", help.OptionGroups[0].Title);
        Assert.AreEqual("Global", help.OptionGroups[1].Title);
        Assert.AreSequenceEqual(["tool"], [.. help.OptionGroups[1].OwnerPath]);
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
        Assert.HasCount(2, document.OptionGroups[0].Options);
        Assert.AreSequenceEqual(["tool"], [.. document.OptionGroups[0].OwnerPath]);
        string rendered = HelpTextRenderer.Render(document, 48);
        Assert.IsTrue(rendered.Split('\n').All(static line => line.Length <= 48));
        Assert.IsLessThan(rendered.IndexOf("Second group:", StringComparison.Ordinal), rendered.IndexOf("First group:", StringComparison.Ordinal));
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

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-005")]
    public void Create_ProjectsDirectivesBeforeTheFirstCommandWithoutSensitiveValueNames()
    {
        DirectiveBuilder debug = new("debug")
        {
            Description = "Enable diagnostic output."
        };
        _ = debug.AddValue(new DirectiveValueDefinition("text", typeof(string)));
        _ = debug.AddValue(new DirectiveValueDefinition("secret", typeof(string), isSensitive: true));
        _ = debug.SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(debug)
            .AddChild(new CommandBuilder("copy")));

        HelpDocument help = HelpDocument.Create(model, model.Root.Children[0]);
        string rendered = HelpTextRenderer.Render(help);

        Assert.AreEqual("tool [DIRECTIVES] copy", help.Usage);
        Assert.AreEqual("[debug <text>]", help.Directives.Single().Syntax);
        Assert.IsTrue(rendered.Contains("Enable diagnostic output.", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("secret", StringComparison.Ordinal));
    }
}
