using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Help;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MSTestMetadata = Mauve.Governance.Testing.MSTest.Metadata;

namespace Mauve.Cli.Generators.Tests;

[TestClass]
public sealed class CliModelGeneratorTests
{
    private const string _validDeclaration = """
        using Mauve.Cli.Declarations;
        using Mauve.Cli.Definitions;
        using Mauve.Cli.Invocation;
        using System.Threading;

        namespace GeneratorFixture;

        [CliCommand("tool", IsRoot = true)]
        [CliChild<Child>]
        public partial class Root
        {
        }

        [CliCommand("copy", Aliases = ["c"])]
        public partial class Child
        {
            [CliOption(IsRequired = true)]
            public int Count { get; init; }

            [CliArgument]
            public string File { get; init; }

            [CliHandler]
            private int Run(CancellationToken cancellationToken) => Count + File.Length;
        }
        """;

    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-16")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-16-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-16-004")]
    public async Task Generate_LowersEnvironmentMetadataForCommandsAndOptionPacks()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            namespace EnvironmentFixture;

            [CliCommand("environment-tool", IsRoot = true)]
            public partial class Root(Pack pack)
            {
                [CliOption(IsRequired = true, EnvironmentVariableName = "CUSTOM_NAME")]
                public required string Name { get; init; }

                [CliHandler]
                private int Run() => Name.Length + pack.Tenant.Length;
            }

            [CliOptionPack]
            public partial class Pack
            {
                [CliOption(IsRequired = true, EnvironmentVariableBinding = EnvironmentVariableBinding.Enabled)]
                public required string Tenant { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assert.IsNotEmpty(
            generated.RunResult.Results.SelectMany(static run => run.GeneratedSources),
            string.Join(Environment.NewLine, generated.RunResult.Results.Select(static run => run.Exception?.ToString())));
        Type rootType = EmitAndLoad(generated.OutputCompilation).GetType("EnvironmentFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        OptionDefinition name = model.Root.Options.Single(static option => option.Name == "--name");
        OptionDefinition tenant = model.Root.Options.Single(static option => option.Name == "--tenant");
        Assert.AreEqual("CUSTOM_NAME", name.EnvironmentVariableName);
        Assert.AreEqual(EnvironmentVariableBinding.Inherit, name.EnvironmentVariableBinding);
        Assert.AreEqual(EnvironmentVariableBinding.Enabled, tenant.EnvironmentVariableBinding);

        CliHostOptions options = new(
            enableEnvironmentVariables: true,
            environmentVariableLookup: static variable => variable switch
            {
                "CUSTOM_NAME" => "Mauve",
                "ENVIRONMENT_TOOL_TENANT" => "workspace",
                _ => null
            });
        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(14, result.ExitCode);
        Assert.AreEqual(ValueOrigin.Environment, result.ParseResult!.Values.Single(value => ReferenceEquals(value.Option, name)).Origin);
        Assert.AreEqual(ValueOrigin.Environment, result.ParseResult.Values.Single(value => ReferenceEquals(value.Option, tenant)).Origin);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public void Generate_UpdatesParentOnlyHierarchyWhenAnEdgeChanges()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            namespace ParentFixture;
            [CliCommand("tool", IsRoot = true)] public partial class Root { }
            [CliCommand("first")] [CliParent<Root>] public partial class First { }
            [CliCommand("second")] [CliParent<Root>] public partial class Second { }
            [CliCommand("leaf")] [CliParent<First>] public partial class Leaf { }
            """;
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation input = CreateCompilation(CSharpSyntaxTree.ParseText(
            declaration, parseOptions, cancellationToken: TestContext.CancellationToken));
        GeneratorDriver driver = CreateDriver(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation initialOutput, out _, TestContext.CancellationToken);
        string[] initialSources = GetGeneratedSources(driver);
        Assert.IsEmpty(GetCompilerErrors(initialOutput));

        driver = driver.RunGenerators(input, TestContext.CancellationToken);
        Assert.AreSequenceEqual(initialSources, GetGeneratedSources(driver));

        string edited = declaration.Replace("CliParent<First>", "CliParent<Second>", StringComparison.Ordinal);
        CSharpCompilation editedInput = CreateCompilation(CSharpSyntaxTree.ParseText(
            edited, parseOptions, cancellationToken: TestContext.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(editedInput, out Compilation editedOutput, out ImmutableArray<Diagnostic> diagnostics, TestContext.CancellationToken);
        Assert.IsFalse(diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsEmpty(GetCompilerErrors(editedOutput));
        Type rootType = EmitAndLoad(editedOutput).GetType("ParentFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.IsEmpty(model.Root.Children[0].Children);
        Assert.AreEqual("leaf", model.Root.Children[1].Children[0].Name);
        Assert.AreNotEqual(string.Join("\n", initialSources), string.Join("\n", GetGeneratedSources(driver)));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public async Task Generate_CombinesParentAndChildEdgesInGlobalSourceOrder()
    {
        const string firstFile = """
            using Mauve.Cli.Declarations;
            namespace MixedFixture;

            [CliCommand("tool", IsRoot = true)]
            [CliChild<Second>]
            public partial class Root { }

            [CliCommand("first")]
            [CliParent<Root>]
            public partial class First { }
            """;
        const string secondFile = """
            using Mauve.Cli.Declarations;
            namespace MixedFixture;

            [CliCommand("third")]
            [CliParent<Root>]
            public partial class Unrelated { }
            """;
        const string thirdFile = """
            using Mauve.Cli.Declarations;
            namespace MixedFixture;

            [CliCommand("second")]
            public partial class Second { }

            [CliCommand("leaf")]
            [CliParent<Second>]
            public partial class Leaf
            {
                [CliArgument]
                public string Value { get; init; }
                [CliHandler]
                private int Run() => Value.Length;
            }
            """;
        GeneratorResult generated = Generate(firstFile, secondFile, thirdFile);
        AssertNoErrors(generated);
        Type rootType = EmitAndLoad(generated.OutputCompilation).GetType("MixedFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.AreSequenceEqual(["second", "first", "third"], model.Root.Children.Select(static child => child.Name));
        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["second", "leaf", "hello"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(5, result.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ReportsBothDeclarationsOfTheSameEdge()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)]
            [CliChild<Leaf>]
            public partial class Root { }
            [CliCommand]
            [CliParent<Root>]
            public partial class Leaf { }
            """;
        GeneratorResult generated = Generate(declaration);
        Diagnostic[] diagnostics = [.. generated.GeneratorDiagnostics.Where(static item => item.Id == "MCLI003")];
        Assert.HasCount(2, diagnostics);
        Assert.AreSequenceEqual(
            ["CliChild<Leaf>", "CliParent<Root>"],
            diagnostics.Select(diagnostic => diagnostic.Location.SourceTree!.GetText(TestContext.CancellationToken).ToString(diagnostic.Location.SourceSpan))
                .OrderBy(static text => text, StringComparer.Ordinal));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
            generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ConflictingEdgeDoesNotInvalidateAnUnrelatedRoot()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] [CliChild<Leaf>] public partial class BadRoot { }
            [CliCommand(IsRoot = true)] [CliChild<Leaf>] public partial class GoodRoot { }
            [CliCommand] [CliParent<BadRoot>] public partial class Leaf { }
            """;
        GeneratorResult generated = Generate(declaration);
        Assert.AreEqual(2, generated.GeneratorDiagnostics.Count(static diagnostic => diagnostic.Id == "MCLI003"));
        Assert.DoesNotContain(static source => source.HintName.Contains("BadRoot", StringComparison.Ordinal),
            generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        Assert.Contains(static source => source.HintName.Contains("GoodRoot", StringComparison.Ordinal),
            generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        Assert.IsEmpty(GetCompilerErrors(generated.OutputCompilation));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsCyclesAcrossAllDeclaredCommands()
    {
        string[] cycles =
        [
            "[CliParent<A>] public partial class A { }",
            "[CliParent<B>] public partial class A { } [CliCommand] [CliParent<A>] public partial class B { }",
            "[CliChild<B>] [CliParent<B>] public partial class A { } [CliCommand] public partial class B { }"
        ];
        foreach (string cycle in cycles)
        {
            string declaration = """
                using Mauve.Cli.Declarations;
                [CliCommand(IsRoot = true)] [CliChild<A>] public partial class Root { }
                [CliCommand]
                """ + cycle;
            GeneratorResult generated = Generate(declaration);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic =>
                diagnostic.Id == "MCLI003" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("cycle", StringComparison.Ordinal)));
            Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
                generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }

        const string disconnected = """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root { }
            [CliCommand] [CliParent<B>] public partial class A { }
            [CliCommand] [CliParent<A>] public partial class B { }
            """;
        GeneratorResult result = Generate(disconnected);
        Assert.IsTrue(result.GeneratorDiagnostics.Any(static diagnostic =>
            diagnostic.Id == "MCLI003" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("cycle", StringComparison.Ordinal)));
        Assert.Contains(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
            result.RunResult.Results.SelectMany(static item => item.GeneratedSources));

        const string rootCycle = """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] [CliChild<A>] [CliParent<A>] public partial class Root { }
            [CliCommand] public partial class A { }
            """;
        GeneratorResult rootResult = Generate(rootCycle);
        Assert.IsTrue(rootResult.GeneratorDiagnostics.Any(static diagnostic =>
            diagnostic.Id == "MCLI003" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("cycle", StringComparison.Ordinal)));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
            rootResult.RunResult.Results.SelectMany(static item => item.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsInvalidParentTargetsAndRootChildren()
    {
        string[] declarations =
        [
            """
            [CliCommand(IsRoot = true)] [CliChild<Leaf>] public partial class Root { }
            [CliCommand] [CliParent<NotCommand>] public partial class Leaf { }
            public class NotCommand { }
            """,
            """
            [CliCommand(IsRoot = true)] [CliParent<Other>] public partial class Root { }
            [CliCommand] public partial class Other { }
            """
        ];
        foreach (string declaration in declarations)
        {
            GeneratorResult generated = Generate("using Mauve.Cli.Declarations;\n" + declaration);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
            Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
                generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-006")]
    public void Generate_InfersXmlMetadataAndShellExamplesWithExplicitModelParity()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            namespace XmlFixture;

            /// <summary>Manage &amp; inspect tasks.</summary>
            /// <remarks>Root details.</remarks>
            [CliCommand("todo", IsRoot = true)]
            [CliChild<ItemCommand>]
            public partial class Root { }

            [CliCommand]
            [CliChild<AddCommand>]
            public partial class ItemCommand { }

            /// <summary>Add a task.</summary>
            /// <remarks>First paragraph.<para>More details.</para></remarks>
            /// <example><code>add --text "A  A"</code></example>
            /// <example><code language="shell">$0 item add --text AAA</code></example>
            /// <example><code language="csharp">
            /// var builder = new AddCommand();
            /// builder.ToString();
            /// </code></example>
            [CliCommand]
            public partial class AddCommand(NetworkPack network)
            {
                /// <summary>Task text.</summary>
                [CliOption(IsRequired = true)]
                public string Text { get; init; }

                /// <summary>Optional <see langword="string"/> label.</summary>
                [CliArgument(MinimumValues = 0)]
                public string? Label { get; init; }
            }

            /// <summary>Network</summary>
            /// <remarks>Network settings.</remarks>
            [CliOptionPack]
            public partial class NetworkPack
            {
                /// <summary>Endpoint address.</summary>
                [CliOption(IsRequired = true)]
                public string Endpoint { get; init; }
            }
            """;
        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        CommandModel model = (CommandModel)fixture.GetType("XmlFixture.Root", throwOnError: true)!
            .GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        CommandDefinition add = model.Root.Children[0].Children[0];
        HelpDocument help = HelpDocument.Create(model, add, "runner");

        CommandBuilder explicitAdd = new("add")
        {
            Description = "Add a task.",
            LongDescription = "First paragraph.\nMore details."
        };
        _ = explicitAdd.AddOption(new OptionDefinition("--text", typeof(string), OptionArity.RequiredScalar,
            isRequired: true, description: "Task text."));
        _ = explicitAdd.AddPositional(new PositionalDefinition("label", typeof(string), minimumValues: 0,
            allowsNull: true, description: "Optional string label."));
        _ = explicitAdd.AddPack("network", new OptionPackBuilder()
            .AddOption(new OptionDefinition("--endpoint", typeof(string), OptionArity.RequiredScalar,
                isRequired: true, description: "Endpoint address.")), "Network", "Network settings.");
        _ = explicitAdd.AddExample("add --text \"A  A\"").AddExample("$0 item add --text AAA");
        CommandModel explicitModel = CommandModel.Create(new CommandBuilder("todo")
            .AddChild(new CommandBuilder("item").AddChild(explicitAdd)));

        Assert.AreEqual("Manage & inspect tasks.", model.Root.Description);
        Assert.AreEqual("Root details.", model.Root.LongDescription);
        Assert.AreEqual("Add a task.", add.Description);
        Assert.AreEqual("First paragraph.\nMore details.", add.LongDescription);
        Assert.AreEqual("Task text.", add.Options[0].Description);
        Assert.AreEqual("Optional string label.", add.Positionals[0].Description);
        Assert.AreEqual("Network", help.OptionGroups[1].Title);
        Assert.AreEqual("Network settings.", help.OptionGroups[1].Description);
        Assert.AreSequenceEqual(["todo item add --text \"A  A\"", "runner item add --text AAA"], [.. help.Examples]);
        Assert.AreEqual(
            HelpTextRenderer.Render(HelpDocument.Create(explicitModel, explicitModel.Root.Children[0].Children[0], "runner")),
            HelpTextRenderer.Render(help));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-006")]
    public void Generate_PreservesExplicitXmlMetadataOverridesIncludingEmptyAndNull()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            namespace XmlOverrides;

            /// <summary>Inferred command summary.</summary>
            /// <remarks>Inferred long description.</remarks>
            [CliCommand("tool", IsRoot = true, Description = "Attribute summary", LongDescription = "")]
            public partial class Root(Pack pack)
            {
                /// <summary>Inferred option.</summary>
                [CliOption(IsRequired = true, Description = "Attribute option")]
                public string First { get; init; }

                /// <summary>Inferred option.</summary>
                [CliOption(IsRequired = true, Description = "")]
                public string Second { get; init; }

                /// <summary>Inferred option.</summary>
                [CliOption(IsRequired = true, Description = null)]
                public string Third { get; init; }

                /// <summary>Inferred argument.</summary>
                [CliArgument(Description = "")]
                public string Argument { get; init; }
            }

            /// <summary>Inferred title.</summary>
            /// <remarks>Inferred pack description.</remarks>
            [CliOptionPack(Title = "Attribute title", Description = "")]
            public partial class Pack
            {
                /// <summary>Inferred pack option.</summary>
                [CliOption(Description = "")]
                public bool Flag { get; init; }
            }
            """;
        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        CommandModel model = (CommandModel)fixture.GetType("XmlOverrides.Root", throwOnError: true)!
            .GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        HelpDocument help = HelpDocument.Create(model);

        Assert.AreEqual("Attribute summary", model.Root.Description);
        Assert.AreEqual(string.Empty, model.Root.LongDescription);
        Assert.AreEqual("Attribute option", model.Root.Options[0].Description);
        Assert.AreEqual(string.Empty, model.Root.Options[1].Description);
        Assert.IsNull(model.Root.Options[2].Description);
        Assert.AreEqual(string.Empty, model.Root.Positionals[0].Description);
        Assert.AreEqual("Attribute title", help.OptionGroups[1].Title);
        Assert.AreEqual(string.Empty, help.OptionGroups[1].Description);
        Assert.AreEqual(string.Empty, help.OptionGroups[1].Options[0].Description);

        GeneratorResult emptyTitle = Generate(declaration.Replace(
            "Title = \"Attribute title\"", "Title = \"\"", StringComparison.Ordinal));
        AssertNoErrors(emptyTitle);
        Assembly emptyTitleFixture = EmitAndLoad(emptyTitle.OutputCompilation);
        CommandModel emptyTitleModel = (CommandModel)emptyTitleFixture.GetType("XmlOverrides.Root", throwOnError: true)!
            .GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.AreEqual(string.Empty, HelpDocument.Create(emptyTitleModel).OptionGroups[1].Title);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-006")]
    public void Generate_RejectsInvalidShellExamplesButIgnoresOtherLanguages()
    {
        string[] examples =
        [
            "<code> </code>",
            "<code language=\"shell\">one\n/// two</code>",
            "<code language=\"shell\"><c>add</c></code>",
            "<code>add &undefined;</code>",
            "<code>add</broken>"
        ];
        foreach (string example in examples)
        {
            GeneratorResult generated = Generate($$"""
                using Mauve.Cli.Declarations;
                /// <example>{{example}}</example>
                [CliCommand("tool", IsRoot = true)]
                public partial class Root { }
                """);
            Assert.Contains(static diagnostic => diagnostic.Id == "MCLI002", generated.GeneratorDiagnostics);
            Assert.IsEmpty(generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }

        GeneratorResult nonShell = Generate("""
            using Mauve.Cli.Declarations;
            /// <example><code language="csharp">
            /// int value = 1;
            /// value++;
            /// </code></example>
            /// <example><code language="other"></code></example>
            [CliCommand("tool", IsRoot = true)]
            public partial class Root { }
            """);
        AssertNoErrors(nonShell);
        Assert.DoesNotContain(static source => source.Contains("command.AddExample", StringComparison.Ordinal),
            nonShell.RunResult.Results.SelectMany(static result => result.GeneratedSources)
                .Select(static source => source.SourceText.ToString()));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-006")]
    public void Generate_UpdatesXmlOnlyEditsWithStableUnchangedOutput()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            /// <summary>Initial description.</summary>
            /// <example><code>$0 --help</code></example>
            [CliCommand("tool", IsRoot = true)]
            public partial class Root { }
            """;
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse);
        CSharpCompilation compilation = CreateCompilation(CSharpSyntaxTree.ParseText(
            declaration, parseOptions, cancellationToken: TestContext.CancellationToken));
        GeneratorDriver driver = CreateDriver(parseOptions).RunGenerators(compilation, TestContext.CancellationToken);
        string[] initial = GetGeneratedSources(driver);
        driver = driver.RunGenerators(compilation, TestContext.CancellationToken);
        Assert.AreSequenceEqual(initial, GetGeneratedSources(driver));
        CSharpCompilation edited = CreateCompilation(CSharpSyntaxTree.ParseText(
            declaration.Replace("Initial description.", "Updated description.", StringComparison.Ordinal)
                .Replace("$0 --help", "$0 --version", StringComparison.Ordinal), parseOptions,
            cancellationToken: TestContext.CancellationToken));
        driver = driver.RunGenerators(edited, TestContext.CancellationToken);
        Assert.Contains(static source => source.Contains("Updated description.", StringComparison.Ordinal)
            && source.Contains("$0 --version", StringComparison.Ordinal), GetGeneratedSources(driver));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-15")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-15-008")]
    public async Task Generate_RegistersCompletionProvidersFromGeneratedModel()
    {
        const string declaration = """
            using System.Collections.Generic;
            using System.IO;
            using System.Threading.Tasks;
            using Mauve.Cli.Completion;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;
            using Mauve.Cli.Invocation;

            namespace CompletionFixture;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root
            {
                [CliOption(IsRequired = true)]
                public string Name { get; init; }
            }

            public static class Fixture
            {
                public static async Task<string> Run()
                {
                    OptionDefinition option = Root.CreateModel().Root.Options[0];
                    CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
                        .Register(option, static (_, _) => Values())
                        .Build();
                    using StringWriter output = new();
                    using StringWriter errors = new();
                    int code = await CliInvoker.RunAsync<Root>(
                        ["[completion:query-v1]", "--", "bash", "tool --name=a", "tool --name=a"],
                        [PredefinedDirectives.CreateCompletion("tool", providers: providers)],
                        new CliHostOptions(output, errors));
                    return $"{code}:{output}:{errors}";
                }

                private static async IAsyncEnumerable<CompletionValue> Values()
                {
                    yield return new CompletionValue("alpha");
                    await Task.CompletedTask;
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type type = fixture.GetType("CompletionFixture.Fixture", throwOnError: true)!;
        Task<string> task = (Task<string>)type.GetMethod("Run")!.Invoke(null, null)!;
        string output = await task.ConfigureAwait(false);

        Assert.AreEqual($"0:xYWxwaGE={Environment.NewLine}:", output);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-003")]
    public async Task Generate_CompilesAndRunsNestedAliasBinding()
    {
        GeneratorResult generated = Generate(_validDeclaration);
        AssertNoErrors(generated);

        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("GeneratorFixture.Root", throwOnError: true)!;
        CommandModel generatedModel = (CommandModel)rootType.GetMethod("CreateModel")!
            .Invoke(null, [null, false])!;
        InvocationResult generatedResult = await new CliInvoker()
            .InvokeAsync(generatedModel, ["c", "--count=3", "file.txt"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        OptionDefinition count = new("--count", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        PositionalDefinition file = new("file", typeof(string));
        CommandModel explicitModel = CommandModel.Create(
            new CommandBuilder("tool").AddChild(
                new CommandBuilder("copy")
                    .AddAlias("c")
                    .AddOption(count)
                    .AddPositional(file)
                    .SetHandler((context, _) =>
                    {
                        Assert.IsTrue(context.TryGetOptionValue(count, out int value));
                        Assert.IsTrue(context.TryGetPositionalValue(file, out string? path));
                        return value + path!.Length;
                    })));
        InvocationResult explicitResult = await new CliInvoker()
            .InvokeAsync(explicitModel, ["c", "--count=3", "file.txt"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, generatedResult.Outcome);
        Assert.AreEqual(explicitResult.Outcome, generatedResult.Outcome);
        Assert.AreEqual(explicitResult.ExitCode, generatedResult.ExitCode);
        Assert.AreEqual("tool copy", string.Join(" ", generatedResult.ParseResult!.SelectedCommand!.Path));
        Assert.AreEqual(11, generatedResult.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    public async Task Generate_CompilesAndAwaitsAllSupportedAsyncHandlerShapes()
    {
        const string declaration = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Invocation;

            namespace AsyncHandlerFixture;

            [CliCommand("tool", IsRoot = true)]
            [CliChild<TaskCommand>]
            [CliChild<TaskWithCodeCommand>]
            [CliChild<ValueTaskCommand>]
            [CliChild<ValueTaskWithCodeCommand>]
            public partial class Root
            {
            }

            [CliCommand]
            public partial class TaskCommand
            {
                [CliHandler]
                private async Task Run()
                {
                    await Task.Yield();
                }
            }

            [CliCommand]
            public partial class TaskWithCodeCommand
            {
                [CliHandler]
                private async Task<int> Run(CancellationToken cancellationToken)
                {
                    await Task.Yield();
                    return cancellationToken.CanBeCanceled ? 17 : -1;
                }
            }

            [CliCommand]
            public partial class ValueTaskCommand
            {
                [CliHandler]
                private async ValueTask Run(CommandInvocationContext context)
                {
                    await Task.Yield();
                    if (context.Command.Name != "value-task")
                    {
                        throw new System.InvalidOperationException("The generated invocation context is incorrect.");
                    }
                }
            }

            [CliCommand]
            public partial class ValueTaskWithCodeCommand
            {
                [CliHandler]
                private async ValueTask<int> Run()
                {
                    await Task.Yield();
                    return 23;
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);

        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("AsyncHandlerFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        using CancellationTokenSource handlerCancellation = new();
        InvocationResult task = await new CliInvoker()
            .InvokeAsync(model, ["task"], TestContext.CancellationToken)
            .ConfigureAwait(false);
        InvocationResult taskWithCode = await new CliInvoker()
            .InvokeAsync(model, ["task-with-code"], handlerCancellation.Token)
            .ConfigureAwait(false);
        InvocationResult valueTask = await new CliInvoker()
            .InvokeAsync(model, ["value-task"], TestContext.CancellationToken)
            .ConfigureAwait(false);
        InvocationResult valueTaskWithCode = await new CliInvoker()
            .InvokeAsync(model, ["value-task-with-code"], TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, task.Outcome);
        Assert.AreEqual(0, task.ExitCode);
        Assert.AreEqual(InvocationOutcome.Completed, taskWithCode.Outcome);
        Assert.AreEqual(17, taskWithCode.ExitCode);
        Assert.AreEqual(InvocationOutcome.Completed, valueTask.Outcome);
        Assert.AreEqual(0, valueTask.ExitCode);
        Assert.AreEqual(InvocationOutcome.Completed, valueTaskWithCode.Outcome);
        Assert.AreEqual(23, valueTaskWithCode.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    public async Task Generate_ForwardCancellationTokenToAsyncHandler()
    {
        const string declaration = """
            using System.Threading;
            using System.Threading.Tasks;
            using Mauve.Cli.Declarations;

            namespace AsyncCancellationFixture;

            [CliCommand("wait", IsRoot = true)]
            public partial class WaitCommand
            {
                [CliHandler]
                private async Task<int> Run(CancellationToken cancellationToken)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    return 1;
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);

        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("AsyncCancellationFixture.WaitCommand", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        using CancellationTokenSource cancellationSource = new();
        Task<InvocationResult> running = new CliInvoker()
            .InvokeAsync(model, [], cancellationSource.Token)
            .AsTask();

        await cancellationSource.CancelAsync().ConfigureAwait(false);
        InvocationResult result = await running.ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-005")]
    public void Generate_ConstrainsSynchronousEntrypointToSynchronousCommandTrees()
    {
        const string syncDeclaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Invocation;
            using System.Threading.Tasks;

            namespace InvocationCapabilityFixture;

            [CliCommand("sync", IsRoot = true)]
            public partial class SyncRoot
            {
                [CliHandler]
                private int Run() => 3;
            }

            [CliCommand("empty", IsRoot = true)]
            public partial class EmptyRoot
            {
            }

            public static class SyncEntrypoints
            {
                public static int RunSync() => CliInvoker.Run<SyncRoot>([]);
                public static Task<int> RunAsync() => CliInvoker.RunAsync<SyncRoot>([]);
                public static int RunHandlerless() => CliInvoker.Run<EmptyRoot>([]);
                public static Task<int> RunWithDirectives() => CliInvoker.RunAsync<SyncRoot>(
                    [], [PredefinedDirectives.CreateDebug(), PredefinedDirectives.CreateCompletion("sync")]);
            }
            """;
        const string asyncDeclaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Invocation;
            using System.Threading.Tasks;

            namespace InvocationCapabilityFixture;

            [CliCommand("async", IsRoot = true)]
            [CliChild<AsyncChild>]
            public partial class AsyncRoot
            {
            }

            [CliCommand("child")]
            public partial class AsyncChild
            {
                [CliHandler]
                private Task Run() => Task.CompletedTask;
            }

            public static class AsyncEntrypoints
            {
                public static Task<int> RunAsync() => CliInvoker.RunAsync<AsyncRoot>([]);
            }
            """;

        GeneratorResult sync = Generate(syncDeclaration);
        GeneratorResult async = Generate(asyncDeclaration);
        AssertNoErrors(sync);
        AssertNoErrors(async);
        Assert.IsEmpty(GetCompilerErrors(sync.OutputCompilation));
        Assert.IsEmpty(GetCompilerErrors(async.OutputCompilation));

        Compilation rejectedSyncCall = AddSource(
            async.OutputCompilation,
            "namespace InvocationCapabilityFixture; public static class InvalidEntrypoint { public static int Run() => Mauve.Cli.Invocation.CliInvoker.Run<AsyncRoot>([]); }");

        Assert.Contains(
            static diagnostic => diagnostic.Id == "CS0311",
            GetCompilerErrors(rejectedSyncCall));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsAsyncVoidAndUnsupportedAsyncResultTypes()
    {
        const string asyncVoidDeclaration = """
            using System.Threading.Tasks;
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class AsyncVoidCommand
            {
                [CliHandler]
                private async void Run()
                {
                    await Task.Yield();
                }
            }
            """;
        const string unsupportedResultDeclaration = """
            using System.Threading.Tasks;
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class UnsupportedResultCommand
            {
                [CliHandler]
                private async Task<string> Run()
                {
                    await Task.Yield();
                    return "unsupported";
                }
            }
            """;

        GeneratorResult asyncVoid = Generate(asyncVoidDeclaration);
        GeneratorResult unsupportedResult = Generate(unsupportedResultDeclaration);

        Assert.IsTrue(asyncVoid.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
        Assert.DoesNotContain(
            static source => source.HintName.Contains("AsyncVoidCommand", StringComparison.Ordinal),
            asyncVoid.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        Assert.IsTrue(unsupportedResult.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
        Assert.DoesNotContain(
            static source => source.HintName.Contains("UnsupportedResultCommand", StringComparison.Ordinal),
            unsupportedResult.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public void Generate_UpdatesAliasesAndProducesStableUnchangedOutput()
    {
        GeneratorDriver driver = CreateDriver();
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree firstTree = CSharpSyntaxTree.ParseText(_validDeclaration, parseOptions, cancellationToken: TestContext.CancellationToken);
        CSharpCompilation firstInput = CreateCompilation(firstTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _, TestContext.CancellationToken);
        string[] initialSources = GetGeneratedSources(driver);

        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _, TestContext.CancellationToken);
        Assert.AreSequenceEqual(initialSources, GetGeneratedSources(driver));

        string changedDeclaration = _validDeclaration.Replace(
            "Aliases = [\"c\"]",
            "Aliases = [\"duplicate\"]",
            StringComparison.Ordinal);
        CSharpCompilation changedInput = CreateCompilation(CSharpSyntaxTree.ParseText(changedDeclaration, parseOptions, cancellationToken: TestContext.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(changedInput, out Compilation changedOutput, out ImmutableArray<Diagnostic> diagnostics, TestContext.CancellationToken);
        Assert.IsFalse(diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsFalse(changedOutput.GetDiagnostics(TestContext.CancellationToken).Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Contains(static source => source.Contains("duplicate", StringComparison.Ordinal), GetGeneratedSources(driver));
        Assert.AreNotEqual(
            string.Join(Environment.NewLine, initialSources),
            string.Join(Environment.NewLine, GetGeneratedSources(driver)));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public void Generate_InfersNamesSourceOrderAndCardinality()
    {
        const string declaration = """
            using System.Collections.Generic;
            using Mauve.Cli.Declarations;

            namespace InferenceFixture;

            [CliCommand(IsRoot = true)]
            [CliChild<HTTPServerCommand>]
            [CliChild<Version2Command>]
            public partial class UploadFilesCommand
            {
                [CliOption(IsRequired = true)]
                public IReadOnlyList<int> ItemIds { get; init; }

                [CliOption]
                public bool DryRun { get; init; }

                [CliArgument]
                public string InputFile { get; init; }

                [CliArgument(MinimumValues = 0)]
                public IReadOnlyList<string> ExtraPaths { get; init; }
            }

            [CliCommand(Description = "")]
            public partial class HTTPServerCommand
            {
            }

            [CliCommand]
            public partial class Version2Command
            {
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("InferenceFixture.UploadFilesCommand", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;

        Assert.AreEqual("upload-files", model.Root.Name);
        Assert.AreSequenceEqual(
            ["http-server", "version2"], [.. model.Root.Children.Select(static child => child.Name)]);
        Assert.AreEqual(string.Empty, model.Root.Children[0].Description);
        Assert.IsNull(model.Root.Children[1].Description);
        Assert.AreSequenceEqual(
            ["--item-ids", "--dry-run"], [.. model.Root.Options.Select(static option => option.Name)]);
        Assert.AreEqual(OptionArity.GreedyCollection, model.Root.Options[0].Arity);
        Assert.AreEqual(OptionArity.BooleanFlag, model.Root.Options[1].Arity);
        Assert.AreSequenceEqual(
            ["input-file", "extra-paths"], [.. model.Root.Positionals.Select(static positional => positional.Name)]);
        Assert.IsNull(model.Root.Positionals[1].MaximumValues);
        Assert.AreEqual(0, model.Root.Positionals[1].MinimumValues);

        ParseResult result = new CliParser().Parse(
            model,
            ["input.txt", "--item-ids", "1", "2", "3", "--item-ids", "4"]);
        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<int>? itemIds));
        Assert.AreSequenceEqual([1, 2, 3, 4], [.. itemIds!]);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-003")]
    public void Generate_SupportsShortOnlyCountingOptions()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            namespace ShortOnlyFixture;

            [CliCommand(IsRoot = true)]
            public partial class RootCommand
            {
                [CliOption(ShortName = 'v', Arity = OptionArity.Counting)]
                public int Verbosity { get; init; }

                [CliOption(ShortName = 'c', Arity = OptionArity.Counting)]
                public int? NullableCount { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("ShortOnlyFixture.RootCommand", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        CliParser parser = new();

        ParseResult repeated = parser.Parse(model, ["-vv", "-c"]);
        ParseResult absent = parser.Parse(model, []);

        Assert.AreEqual(ParseOutcome.Validated, repeated.Outcome, string.Join(" | ", repeated.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        Assert.IsNull(model.Root.Options[0].LongName);
        Assert.AreEqual('v', model.Root.Options[0].ShortName);
        Assert.IsTrue(repeated.TryGetOptionValue(model.Root, model.Root.Options[0], out int verbosity));
        Assert.AreEqual(2, verbosity);
        Assert.IsTrue(repeated.TryGetOptionValue(model.Root, model.Root.Options[1], out int? nullableCount));
        Assert.AreEqual(1, nullableCount);
        Assert.IsTrue(absent.TryGetOptionValue(model.Root, model.Root.Options[0], out int absentVerbosity));
        Assert.AreEqual(0, absentVerbosity);
        Assert.IsTrue(absent.TryGetOptionValue(model.Root, model.Root.Options[1], out int? absentNullableCount));
        Assert.IsNull(absentNullableCount);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public void Generate_OrdersPartialMembersByCompilationSyntaxTreeThenSpan()
    {
        const string first = """
            using Mauve.Cli.Declarations;

            namespace PartialOrderFixture;

            [CliCommand(IsRoot = true)]
            public partial class RootCommand
            {
                [CliOption(IsRequired = true)]
                public string FirstTreeFirst { get; init; }

                [CliOption(IsRequired = true)]
                public string FirstTreeSecond { get; init; }
            }
            """;
        const string second = """
            using Mauve.Cli.Declarations;

            namespace PartialOrderFixture;

            public partial class RootCommand
            {
                [CliOption(IsRequired = true)]
                public string SecondTree { get; init; }
            }
            """;

        GeneratorResult generated = Generate(first, second);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("PartialOrderFixture.RootCommand", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;

        Assert.AreSequenceEqual(
            ["--first-tree-first", "--first-tree-second", "--second-tree"], [.. model.Root.Options.Select(static option => option.Name)]);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ValidatesExplicitInferenceOverrides()
    {
        const string declaration = """
            using System.Collections.Generic;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            [CliCommand(IsRoot = true)]
            public partial class ToolCommand
            {
                [CliOption("--numbers", Arity = OptionArity.RepeatedSingleCollection, IsRequired = true)]
                public IReadOnlyList<int> Values { get; init; }

                [CliArgument(MinimumValues = 0, MaximumValues = -1)]
                public IReadOnlyList<string> Paths { get; init; }
            }
            """;
        const string invalidDeclaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            [CliCommand(IsRoot = true)]
            public partial class ToolCommand
            {
                [CliOption(Arity = OptionArity.BooleanFlag, IsRequired = true)]
                public string Value { get; init; }
            }
            """;
        const string invalidNameDeclaration = """
            using Mauve.Cli.Declarations;

            [CliCommand(IsRoot = true)]
            public partial class Command
            {
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("ToolCommand", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.AreEqual("--numbers", model.Root.Options[0].Name);
        Assert.AreEqual(OptionArity.RepeatedSingleCollection, model.Root.Options[0].Arity);
        Assert.AreEqual(0, model.Root.Positionals[0].MinimumValues);
        Assert.IsNull(model.Root.Positionals[0].MaximumValues);

        GeneratorResult invalid = Generate(invalidDeclaration);
        Assert.IsTrue(invalid.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
        GeneratorResult invalidName = Generate(invalidNameDeclaration);
        Assert.IsTrue(invalidName.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI001"));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ReportsUnsupportedMembersAndEmitsNoInvalidRoot()
    {
        const string invalidDeclaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root
            {
                [CliOption(IsRequired = true)]
                public int Count { get; }
            }
            """;

        GeneratorResult generated = Generate(invalidDeclaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ReportsDuplicateSiblingAliasesAndEmitsNoRoot()
    {
        const string invalidDeclaration = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            [CliChild<First>]
            [CliChild<Second>]
            public partial class Root
            {
            }

            [CliCommand("first", Aliases = ["same"])]
            public partial class First
            {
            }

            [CliCommand("second", Aliases = ["same"])]
            public partial class Second
            {
            }
            """;

        GeneratorResult generated = Generate(invalidDeclaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public async Task Generate_BuildsPackModelEquivalentToExplicitModel()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;
            using Mauve.Cli.Invocation;

            namespace GeneratorPackFixture;

            [CliCommand("fetch", IsRoot = true)]
            public partial class Root(NetworkPack network)
            {
                private readonly string _endpointAtConstruction = network.Endpoint;

                [CliHandler]
                private int Run()
                {
                    string address = network.Endpoint;
                    int count = network.Retry.Retries;
                    if (address != _endpointAtConstruction
                        || count != network.RetriesAtConstruction)
                    {
                        throw new System.InvalidOperationException("The generated pack values are unavailable.");
                    }

                    return address.Length + count;
                }
            }

            [CliOptionPack(Title = "Network", Description = "Network settings")]
            public partial class NetworkPack(RetryPack retry)
            {
                public RetryPack Retry { get; } = retry;
                public int RetriesAtConstruction { get; } = retry.Retries;

                [CliOption(IsRequired = true)]
                public string Endpoint { get; init; }
            }

            [CliOptionPack(Title = "Retry", Description = "Retry settings")]
            public partial class RetryPack
            {
                [CliOption(IsRequired = true)]
                public int Retries { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("GeneratorPackFixture.Root", throwOnError: true)!;
        CommandModel generatedModel = (CommandModel)rootType.GetMethod("CreateModel")!
            .Invoke(null, [null, false])!;
        CommandBuilder generatedBuilder = (CommandBuilder)rootType.GetMethod("CreateBuilder")!
            .Invoke(null, null)!;
        OptionDefinition manualOption = new("--profile", typeof(string), OptionArity.RequiredScalar);
        generatedBuilder.AddOption(manualOption);
        CommandModel generatedWithManualOption = CommandModel.Create(generatedBuilder);
        ParseResult generatedWithManualOptionResult = new CliParser().Parse(
            generatedWithManualOption,
            ["--endpoint=https://example.test", "--retries=2", "--profile=fast"]);
        OptionDefinition generatedEndpoint = generatedModel.Root.Options.Single(static option => option.Name == "--endpoint");
        OptionDefinition generatedRetries = generatedModel.Root.Options.Single(static option => option.Name == "--retries");
        InvocationResult generatedInvocation = await new CliInvoker().InvokeAsync(
            generatedModel,
            ["--endpoint=https://example.test", "--retries=2"], TestContext.CancellationToken).ConfigureAwait(false);
        ParseResult generatedResult = generatedInvocation.ParseResult!;

        OptionDefinition endpoint = new("--endpoint", typeof(string), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition retries = new("--retries", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        OptionPackBuilder retryPack = new OptionPackBuilder().AddOption(retries);
        OptionPackBuilder networkPack = new OptionPackBuilder()
            .AddOption(endpoint)
            .AddPack("retry", retryPack, "Retry", "Retry settings");
        CommandModel explicitModel = CommandModel.Create(
            new CommandBuilder("fetch")
                .AddPack("network", networkPack, "Network", "Network settings")
                .SetHandler((context, _) =>
                {
                    OptionPackContext network = context.GetOptionPack("network");
                    OptionPackContext retry = network.GetNestedPack("retry");
                    return network.TryGetOptionValue(endpoint, out string? address)
                        && retry.TryGetOptionValue(retries, out int count)
                            ? address!.Length + count
                            : throw new InvalidOperationException("The explicit pack values are unavailable.");
                }));
        InvocationResult explicitInvocation = await new CliInvoker().InvokeAsync(
            explicitModel,
            ["--endpoint=https://example.test", "--retries=2"], TestContext.CancellationToken).ConfigureAwait(false);
        ParseResult explicitResult = explicitInvocation.ParseResult!;

        Assert.AreEqual(InvocationOutcome.Completed, generatedInvocation.Outcome);
        Assert.AreEqual(explicitInvocation.Outcome, generatedInvocation.Outcome);
        Assert.AreEqual(explicitInvocation.ExitCode, generatedInvocation.ExitCode);
        Assert.AreEqual(ParseOutcome.Validated, generatedResult.Outcome);
        Assert.AreEqual(explicitResult.Outcome, generatedResult.Outcome);
        Assert.IsTrue(generatedResult.TryGetOptionValue(generatedModel.Root, generatedEndpoint, out string? generatedAddress));
        Assert.IsTrue(generatedResult.TryGetOptionValue(generatedModel.Root, generatedRetries, out int generatedCount));
        Assert.IsTrue(explicitResult.TryGetOptionValue(explicitModel.Root, endpoint, out string? explicitAddress));
        Assert.IsTrue(explicitResult.TryGetOptionValue(explicitModel.Root, retries, out int explicitCount));
        Assert.AreEqual(explicitAddress, generatedAddress);
        Assert.AreEqual(explicitCount, generatedCount);
        Assert.IsTrue(generatedWithManualOptionResult.TryGetOptionValue(
            generatedWithManualOption.Root,
            manualOption,
            out string? profile));
        Assert.AreEqual("fast", profile);
        Assert.AreSequenceEqual(
            [.. explicitResult.SelectedCommand!.Path], [.. generatedResult.SelectedCommand!.Path]);
        Assert.AreSequenceEqual(
            [.. HelpDocument.Create(explicitModel).OptionGroups.Select(static group => group.Title)], [.. HelpDocument.Create(generatedModel).OptionGroups.Select(static group => group.Title)]);
        Assert.AreSequenceEqual(
            [.. HelpDocument.Create(explicitModel).OptionGroups.Select(static group => group.Description)],
            [.. HelpDocument.Create(generatedModel).OptionGroups.Select(static group => group.Description)]);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-005")]
    public async Task Generate_InjectsInvocationLocalNestedPacksWithoutConstructingDuringParsing()
    {
        const string declaration = """
            using System;
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using Mauve.Cli.Completion;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;
            using Mauve.Cli.Help;
            using Mauve.Cli.Invocation;
            using Mauve.Cli.Parsing;

            namespace PackIsolationFixture;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Network network)
            {
                public static int Created;
                private readonly int _id = Interlocked.Increment(ref Created);

                [CliHandler]
                private async Task<int> Run()
                {
                    string name = network.Name;
                    int retries = network.Retry.Retries;
                    IReadOnlyList<string> tags = network.Tags;
                    string? label = network.Label;
                    await Fixture.ArriveAsync();
                    if (network.Name != name || network.Retry.Retries != retries
                        || !network.Tags.SequenceEqual(tags)
                        || network.Label != label)
                    {
                        throw new InvalidOperationException("Pack values changed across invocations.");
                    }

                    Fixture.Results[name] = (_id, network.Id, network.Retry.Id, retries, label, tags);
                    return retries;
                }
            }

            [CliOptionPack]
            public partial class Network(Retry retry)
            {
                public static int Created;
                public int Id { get; } = Interlocked.Increment(ref Created);
                public Retry Retry { get; } = retry;

                [CliOption(IsRequired = true)]
                public string Name { get; init; }

                [CliOption]
                public string? Label { get; init; }

                [CliOption(IsRequired = true)]
                public IReadOnlyList<string> Tags { get; init; }
            }

            [CliOptionPack]
            public partial class Retry
            {
                public static int Created;
                public int Id { get; } = Interlocked.Increment(ref Created);

                [CliOption(IsRequired = true)]
                public int Retries { get; init; }
            }

            public static class Fixture
            {
                private static readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                private static int _arrived;
                public static ConcurrentDictionary<string, (int Root, int Network, int Retry, int Count, string? Label, IReadOnlyList<string> Tags)> Results { get; } = new();

                public static Task ArriveAsync()
                {
                    if (Interlocked.Increment(ref _arrived) == 2)
                    {
                        Ready.SetResult();
                    }

                    return Ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }

                public static async Task<string> Run()
                {
                    CommandModel model = Root.CreateModel();
                    _ = HelpDocument.Create(model);
                    _ = await new CompletionService().CompleteAsync(new CompletionRequest(model, ["--na"], 0, 4));
                    ParseResult valid = new CliParser().Parse(model, ["--name=parsed", "--retries=3", "--tags=parsed"]);
                    ParseResult invalid = new CliParser().Parse(model, ["--name=missing-retries", "--tags=parsed"]);
                    InvocationResult rejected = await new CliInvoker().InvokeAsync(model, ["--name=missing-retries", "--tags=parsed"]);
                    if (valid.Outcome != ParseOutcome.Validated || invalid.Outcome == ParseOutcome.Validated
                        || rejected.Outcome == InvocationOutcome.Completed
                        || Root.Created != 0 || Network.Created != 0 || Retry.Created != 0)
                    {
                        throw new InvalidOperationException("Parsing or a rejected invocation constructed application objects.");
                    }

                    CliInvoker invoker = new();
                    Task<InvocationResult> first = invoker.InvokeAsync(model, ["--name=alpha", "--retries=1", "--label=first", "--tags=one"]).AsTask();
                    Task<InvocationResult> second = invoker.InvokeAsync(model, ["--name=beta", "--retries=2", "--tags=two"]).AsTask();
                    InvocationResult[] outcomes = await Task.WhenAll(first, second);
                    var alpha = Results["alpha"];
                    var beta = Results["beta"];
                    if (outcomes.Any(static outcome => outcome.Outcome != InvocationOutcome.Completed)
                        || outcomes[0].ExitCode != 1 || outcomes[1].ExitCode != 2
                        || alpha.Root == beta.Root || alpha.Network == beta.Network || alpha.Retry == beta.Retry
                        || alpha.Count != 1 || beta.Count != 2 || alpha.Label != "first" || beta.Label is not null
                        || ReferenceEquals(alpha.Tags, beta.Tags) || !alpha.Tags.SequenceEqual(["one"]) || !beta.Tags.SequenceEqual(["two"])
                        || Root.Created != 2 || Network.Created != 2 || Retry.Created != 2)
                    {
                        throw new InvalidOperationException("Pack instances or values were not isolated.");
                    }

                    return "isolated";
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type type = fixture.GetType("PackIsolationFixture.Fixture", throwOnError: true)!;
        Task<string> task = (Task<string>)type.GetMethod("Run")!.Invoke(null, null)!;

        Assert.AreEqual("isolated", await task.ConfigureAwait(false));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    public async Task Generate_OrdersInjectedPacksAndIgnoresParameterNames()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            namespace PackOrderFixture;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(First first, Second second)
            {
                [CliHandler]
                private int Run() => first.Enabled && second.Forced ? 1 : 0;
            }

            [CliOptionPack(Title = "First")]
            public partial class First
            {
                [CliOption]
                public bool Enabled { get; init; }
            }

            [CliOptionPack(Title = "Second")]
            public partial class Second
            {
                [CliOption]
                public bool Forced { get; init; }
            }
            """;

        GeneratorResult original = Generate(declaration);
        GeneratorResult renamed = Generate(declaration
            .Replace("First first", "First renamed", StringComparison.Ordinal)
            .Replace("first.Enabled", "renamed.Enabled", StringComparison.Ordinal));
        AssertNoErrors(original);
        AssertNoErrors(renamed);
        Type firstRoot = EmitAndLoad(original.OutputCompilation).GetType("PackOrderFixture.Root", throwOnError: true)!;
        Type secondRoot = EmitAndLoad(renamed.OutputCompilation).GetType("PackOrderFixture.Root", throwOnError: true)!;
        CommandModel firstModel = (CommandModel)firstRoot.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        CommandModel secondModel = (CommandModel)secondRoot.GetMethod("CreateModel")!.Invoke(null, [null, false])!;

        Assert.AreSequenceEqual(["--enabled", "--forced"], [.. firstModel.Root.Options.Select(static option => option.Name)]);
        Assert.AreSequenceEqual(
            [.. HelpDocument.Create(firstModel).OptionGroups.Select(static group => group.Key)],
            [.. HelpDocument.Create(secondModel).OptionGroups.Select(static group => group.Key)]);
        InvocationResult result = await new CliInvoker().InvokeAsync(
            secondModel, ["--enabled", "--forced"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(1, result.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public async Task Generate_InitializesPackPropertiesAfterItsConstructor()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            namespace PackConstructionFixture;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Network network)
            {
                [CliHandler]
                private int Run() => network.Enabled ? 1 : 0;
            }

            [CliOptionPack]
            public partial class Network
            {
                public Network() => _ = Enabled;

                [CliOption]
                public bool Enabled { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Type root = EmitAndLoad(generated.OutputCompilation).GetType("PackConstructionFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;

        InvocationResult result = await new CliInvoker().InvokeAsync(model, ["--enabled"], TestContext.CancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(1, result.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-12")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-12-002")]
    public async Task Generate_ExposesRootPackInheritedOptionBeforeAndAfterChild()
    {
        const string declaration = """
            using System.Linq;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;
            using Mauve.Cli.Invocation;

            namespace InheritedPackFixture;

            [CliCommand("tool", IsRoot = true)]
            [CliChild<Show>]
            public partial class Root(Global global)
            {
                public Global Global { get; } = global;
            }

            [CliOptionPack]
            public partial class Global
            {
                [CliOption(IsRequired = true, Scope = OptionScope.Inherited)]
                public string Environment { get; init; }
            }

            [CliCommand]
            public partial class Show
            {
                [CliHandler]
                private int Run(CommandInvocationContext context)
                {
                    OptionDefinition option = context.ParseResult.SelectedPath[0].Options
                        .Single(static value => value.Name == "--environment");
                    return context.TryGetOptionValue(option, out string? value) && value == "dev" ? 7 : 0;
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Type root = EmitAndLoad(generated.OutputCompilation).GetType("InheritedPackFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        CliInvoker invoker = new();
        InvocationResult before = await invoker.InvokeAsync(
            model, ["--environment=dev", "show"], TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult after = await invoker.InvokeAsync(
            model, ["show", "--environment=dev"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, before.Outcome);
        Assert.AreEqual(7, before.ExitCode);
        Assert.AreEqual(InvocationOutcome.Completed, after.Outcome);
        Assert.AreEqual(7, after.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public void Generate_RejectsDuplicatePackAliasesWithoutRegisteringRoot()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(FirstPack first, SecondPack second)
            {
            }

            [CliOptionPack]
            public partial class FirstPack
            {
                [CliOption(IsRequired = true, Aliases = ["-x"])]
                public string First { get; init; }
            }

            [CliOptionPack]
            public partial class SecondPack
            {
                [CliOption(IsRequired = true, Aliases = ["-x"])]
                public string Second { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public void Generate_RejectsPackCyclesWithoutRegisteringRoot()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(FirstPack first)
            {
            }

            [CliOptionPack]
            public partial class FirstPack(SecondPack second)
            {
            }

            [CliOptionPack]
            public partial class SecondPack(FirstPack first)
            {
            }
            """;
        const string selfCycle = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack pack) { }

            [CliOptionPack]
            public partial class Pack(Pack nested) { }
            """;

        foreach (string source in new[] { declaration, selfCycle })
        {
            GeneratorResult generated = Generate(source);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
            Assert.DoesNotContain(static generatedSource => generatedSource.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public async Task Generate_AllowsPackTypeReuseOnDisjointCommands()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            namespace PackReuseFixture;

            [CliCommand("tool", IsRoot = true)]
            [CliChild<First>]
            [CliChild<Second>]
            public partial class Root { }

            [CliCommand("first")]
            public partial class First(Shared pack)
            {
                [CliHandler]
                private int Run() => pack.Value;
            }

            [CliCommand("second")]
            public partial class Second(Shared pack)
            {
                [CliHandler]
                private int Run() => pack.Value;
            }

            [CliOptionPack]
            public partial class Shared
            {
                [CliOption(IsRequired = true)]
                public int Value { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type type = fixture.GetType("PackReuseFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)type.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        CliInvoker invoker = new();
        InvocationResult first = await invoker.InvokeAsync(model, ["first", "--value=7"], TestContext.CancellationToken).ConfigureAwait(false);
        InvocationResult second = await invoker.InvokeAsync(model, ["second", "--value=9"], TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, first.Outcome);
        Assert.AreEqual(7, first.ExitCode);
        Assert.AreEqual(InvocationOutcome.Completed, second.Outcome);
        Assert.AreEqual(9, second.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public void Generate_RejectsDuplicateDirectPackTypesWithoutRegisteringRoot()
    {
        const string duplicateCommandPacks = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack first, Pack second) { }

            [CliOptionPack]
            public partial class Pack { }
            """;
        const string duplicateNestedPacks = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Outer outer) { }

            [CliOptionPack]
            public partial class Outer(Pack first, Pack second) { }

            [CliOptionPack]
            public partial class Pack { }
            """;

        foreach (string declaration in new[] { duplicateCommandPacks, duplicateNestedPacks })
        {
            GeneratorResult generated = Generate(declaration);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
            Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsUnsupportedPackConstructorsWithoutRegisteringRoot()
    {
        const string unsupportedParameter = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack pack) { }

            [CliOptionPack]
            public partial class Pack(string value) { }
            """;
        const string ambiguousConstructors = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack pack) { }

            [CliOptionPack]
            public partial class Pack
            {
                public Pack() { }
                public Pack(Nested nested) { }
            }

            [CliOptionPack]
            public partial class Nested { }
            """;

        foreach (string declaration in new[] { unsupportedParameter, ambiguousConstructors })
        {
            GeneratorResult generated = Generate(declaration);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
            Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public void Generate_RejectsUnsupportedPackPropertiesWithoutRegisteringRoot()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack pack) { }

            [CliOptionPack]
            public partial class Pack
            {
                [CliOption]
                public bool NotPartial { get; }

                [CliArgument]
                public string Positional { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        Assert.AreEqual(2, generated.GeneratorDiagnostics.Count(static diagnostic => diagnostic.Id == "MCLI002"));
        Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal), generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-15")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-15-004")]
    public async Task Generate_RegistersStandaloneAndRootMethodDirectives()
    {
        const string declaration = """
            using System.Threading;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Invocation;

            namespace DirectiveFixture;

            [CliCommand("tool", IsRoot = true)]
            [CliDirective<DebugDirective>]
            public partial class Root
            {
                [CliDirective("version")]
                private DirectiveResult Version(CancellationToken cancellationToken)
                    => cancellationToken.CanBeCanceled
                        ? DirectiveResult.StopProcessing(9)
                        : DirectiveResult.StopProcessing(8);
            }

            [CliDirective]
            public partial class DebugDirective
            {
                public static string? Captured { get; private set; }

                [CliOption(IsRequired = true)]
                public partial string Text { get; }

                [CliHandler]
                private DirectiveResult Run()
                {
                    Captured = Text;
                    return DirectiveResult.ContinueProcessing;
                }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("DirectiveFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)rootType.GetMethod("CreateModel")!
            .Invoke(null, [null, false])!;
        InvocationResult result = await new CliInvoker()
            .InvokeAsync(model, ["[debug:first]", "[version]", "[debug:last]"], TestContext.CancellationToken)
            .ConfigureAwait(false);
        Type directiveType = fixture.GetType("DirectiveFixture.DebugDirective", throwOnError: true)!;

        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(9, result.ExitCode);
        Assert.AreEqual("first", directiveType.GetProperty("Captured")!.GetValue(null));
        Assert.HasCount(3, result.ParseResult!.Directives);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-003")]
    public async Task Generate_BindsTypeAndPropertyConvertersEnumsAndOrderedUnions()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Globalization;
            using Mauve.Cli.Conversion;
            using Mauve.Cli.Declarations;
            namespace ConversionFixture;

            [CliConverter<CodeConverter>]
            public readonly record struct Code(int Value);
            public sealed class CodeConverter : ICliValueConverter<Code>
            {
                public static ValueConversion<Code> Convert(string value, ConversionContext context)
                    => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                        ? ValueConversion.FromValue(new Code(number)) : ValueConversion.Failure<Code>("code.invalid");
            }
            public sealed class HexConverter : ICliValueConverter<int>
            {
                public static ValueConversion<int> Convert(string value, ConversionContext context)
                    => int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int number)
                        ? ValueConversion.FromValue(number) : ValueConversion.Failure<int>("hex.invalid");
            }
            [CliConverter<TokenConverter>]
            public sealed record Token(string Value);
            public sealed class TokenConverter : ICliValueConverter<Token>
            {
                public static ValueConversion<Token> Convert(string value, ConversionContext context)
                    => ValueConversion.FromValue(new Token(value));
            }
            public sealed class NullableCodeConverter : ICliValueConverter<Code?>
            {
                public static ValueConversion<Code?> Convert(string value, ConversionContext context)
                    => value == "none" ? ValueConversion.FromValue<Code?>(null)
                        : ValueConversion.FromValue<Code?>(new Code(7));
            }
            public enum Mode { Slow, Fast }
            public union Choice(int, string);
            [CliCommand(IsRoot = true)]
            [CliDirective<CodeDirective>]
            public partial class Root
            {
                [CliOption(IsRequired = true)] public Code Code { get; init; }
                [CliOption(IsRequired = true)] [CliConverter<HexConverter>] public int Hex { get; init; }
                [CliOption(IsRequired = true)] public Mode? Mode { get; init; }
                [CliOption(IsRequired = true)] public Choice Choice { get; init; }
                [CliOption] public Token? Token { get; init; }
                [CliOption] [CliConverter<NullableCodeConverter>] public IReadOnlyList<Code?> OptionalCodes { get; init; }
                [CliArgument] public IReadOnlyList<Code?> Codes { get; init; }
                [CliHandler]
                private int Run() => Code.Value + Hex + (int)Mode!.Value + Codes[0]!.Value.Value
                    + (Choice.Value is int number ? number : -1000)
                    + (Token?.Value.Length ?? 0);
            }
            [CliDirective]
            public partial class CodeDirective
            {
                [CliOption(IsRequired = true)] [CliConverter<CodeConverter>] public partial Code Code { get; }
                [CliHandler] private int Run() => Code.Value;
            }
            public static class Host
            {
                public static int Run()
                    => Mauve.Cli.Invocation.CliInvoker.Run<Root>(
                        ["--code=12", "--hex=FF", "--mode=Fast", "--choice=42", "23"],
                        new Mauve.Cli.Invocation.CliHostOptions(converters:
                            new ConverterRegistryBuilder().Register<int>(static (_, _) => ValueConversion.FromValue(17)).Build()));
            }
            """;
        GeneratorResult generated = Generate(source);
        AssertNoErrors(generated);
        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type root = fixture.GetType("ConversionFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["--code=12", "--hex=FF", "--mode=Fast", "--choice=42", "23"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual(333, result.ExitCode);
        Assert.AreEqual(308, fixture.GetType("ConversionFixture.Host", throwOnError: true)!.GetMethod("Run")!.Invoke(null, null));
        InvocationResult reference = await new CliInvoker().InvokeAsync(
            model, ["--code=12", "--hex=FF", "--mode=Fast", "--choice=42", "--token=abc", "23"],
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(336, reference.ExitCode);
        ParseResult nullableElements = new CliParser().Parse(model,
            ["--code=12", "--hex=FF", "--mode=Fast", "--choice=42", "--optional-codes=none", "--optional-codes=7", "23"]);
        Assert.AreEqual(ParseOutcome.Validated, nullableElements.Outcome);
        Assert.IsTrue(nullableElements.TryGetOptionValue(model.Root, model.Root.Options[5], out System.Collections.IEnumerable? nullableCodes));
        Assert.IsNull(nullableCodes!.Cast<object?>().First());
        InvocationResult directive = await new CliInvoker().InvokeAsync(
            model, ["[code:7]"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(7, directive.ExitCode);
        ParseResult invalid = new CliParser().Parse(model, ["--code=bad", "--hex=FF", "--mode=Fast", "--choice=42", "23"]);
        Assert.AreEqual(ParseOutcome.Invalid, invalid.Outcome);
        Assert.IsEmpty(invalid.Values);

        GeneratorResult unchanged = Generate(source);
        Assert.AreSequenceEqual(
            generated.RunResult.Results.SelectMany(static result => result.GeneratedSources).Select(static result => result.SourceText.ToString()),
            unchanged.RunResult.Results.SelectMany(static result => result.GeneratedSources).Select(static result => result.SourceText.ToString()));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsMismatchedProvidersAndCustomUnionDiscovery()
    {
        const string mismatched = """
            using Mauve.Cli.Conversion;
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root
            {
                [CliOption(IsRequired = true)] [CliConverter<TextConverter>] public int Value { get; init; }
            }
            public sealed class TextConverter : ICliValueConverter<string>
            {
                public static ValueConversion<string> Convert(string value, ConversionContext context)
                    => ValueConversion.FromValue(value);
            }
            """;
        const string customUnion = """
            using Mauve.Cli.Declarations;
            using System.Runtime.CompilerServices;
            [Union]
            public struct Custom : IUnion
            {
                public Custom(int value) { Value = value; }
                public object? Value { get; }
            }
            [CliCommand(IsRoot = true)] public partial class Root
            {
                [CliOption(IsRequired = true)] public Custom Value { get; init; }
            }
            """;
        foreach (string source in new[] { mismatched, customUnion })
        {
            GeneratorResult generated = Generate(source);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
            Assert.IsEmpty(generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-007")]
    public async Task Generate_InfersRequiredAndConstantDefaultsForCommandsAndPacks()
    {
        const string declaration = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.Linq;
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;
            using Mauve.Cli.Invocation;

            namespace DefaultFixture;
            public enum Mode { Fast = 2, Slow = 3 }

            [CliCommand("tool", IsRoot = true)]
            public partial class Root(Pack pack)
            {
                public static string? Observed { get; private set; }

                [Required]
                [CliOption]
                public string Name { get; init; }

                [Required]
                [CliOption(IsRequired = false)]
                public required string? Override { get; init; }

                [CliOption]
                public string Title { get; init; } = "default value";

                [CliOption]
                public int Count { get; init; } = 42;

                [CliOption]
                public Mode Mode { get; init; } = Mode.Fast;

                [CliOption(Arity = OptionArity.RequiredScalar)]
                public bool Switch { get; init; } = true;

                [CliOption(IsSensitive = true)]
                public string Secret { get; init; } = "secret-default";

                [CliOption(IsRequired = true)]
                public string Inert { get; init; } = "inert-default";

                [CliArgument(MinimumValues = 0)]
                [Required]
                public required string Path { get; init; } = "fallback";

                [CliHandler]
                private int Run(CommandInvocationContext context)
                {
                    var title = context.ParseResult.Values.Single(value => value.Option?.Name == "--title");
                    Observed = $"{Name}|{Override ?? "(absent)"}|{Title}|{Count}|{Mode}|{Switch}|{Secret}|{Inert}|{Path}|{pack.Tenant}|{title.Origin}|{title.Presence}";
                    return 0;
                }
            }

            [CliOptionPack]
            public partial class Pack
            {
                [CliOption]
                public required string Tenant { get; init; }
            }
            """;

        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Type root = EmitAndLoad(generated.OutputCompilation).GetType("DefaultFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        OptionDefinition name = model.Root.Options.Single(static option => option.Name == "--name");
        OptionDefinition tenant = model.Root.Options.Single(static option => option.Name == "--tenant");
        OptionDefinition optional = model.Root.Options.Single(static option => option.Name == "--override");
        OptionDefinition title = model.Root.Options.Single(static option => option.Name == "--title");
        Assert.IsTrue(name.IsRequired);
        Assert.IsTrue(tenant.IsRequired);
        Assert.IsFalse(optional.IsRequired);
        Assert.AreEqual(0, model.Root.Positionals[0].MinimumValues);
        Assert.AreEqual(DefaultValuePolicy.Constant, title.DefaultValuePolicy);
        Assert.AreEqual("default value", title.DefaultValue);
        Assert.AreEqual("Fast", model.Root.Options.Single(static option => option.Name == "--mode").DefaultDisplay);
        Assert.AreEqual("true", model.Root.Options.Single(static option => option.Name == "--switch").DefaultDisplay);

        string help = HelpTextRenderer.Render(HelpDocument.Create(model));
        Assert.Contains("default value", help);
        Assert.DoesNotContain("secret-default", help);
        Assert.DoesNotContain("inert-default", help);
        Assert.AreNotEqual(ParseOutcome.Validated, new CliParser().Parse(model, []).Outcome);
        Assert.AreNotEqual(ParseOutcome.Validated, new CliParser().Parse(model, ["--name=alice", "--tenant=west"]).Outcome);

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, ["--name=alice", "--tenant=west", "--inert=given"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual("alice|(absent)|default value|42|Fast|True|secret-default|given|fallback|west|Default|Absent",
            root.GetProperty("Observed")!.GetValue(null));

        result = await new CliInvoker().InvokeAsync(
            model, ["--name=alice", "--tenant=west", "--inert=given", "--title=changed", "input"],
            TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.Completed, result.Outcome);
        Assert.AreEqual("alice|(absent)|changed|42|Fast|True|secret-default|given|input|west|Explicit|WithValue",
            root.GetProperty("Observed")!.GetValue(null));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-007")]
    public void Generate_RequiresPositionalWithRequiredModifier()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;
            [CliCommand("tool", IsRoot = true)]
            public partial class Root {
                [CliArgument] public required string Value { get; init; }
            }
            """;
        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Type root = EmitAndLoad(generated.OutputCompilation).GetType("Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.AreEqual(1, model.Root.Positionals[0].MinimumValues);
        Assert.AreNotEqual(ParseOutcome.Validated, new CliParser().Parse(model, []).Outcome);
        Assert.AreEqual(ParseOutcome.Validated, new CliParser().Parse(model, ["value"]).Outcome);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-007")]
    public void Generate_UsesTypedInvariantConstantDisplaysAndNullableDefaults()
    {
        const string declaration = """
            using System;
            using Mauve.Cli.Declarations;
            namespace ConstantFixture;
            public enum Level { Low = 1, High = 2 }
            [CliCommand("tool", IsRoot = true)]
            public partial class Root {
                [CliOption] public char Letter { get; init; } = 'x';
                [CliOption] public decimal Price { get; init; } = 0.1234567890123456789012345678m;
                [CliOption] public double Number { get; init; } = 9007199254740993L;
                [CliOption] public Level Level { get; init; } = (Level)5;
                [CliOption] public Guid? Id { get; init; } = null;
                [CliOption] public string? Text { get; init; } = null;
                [CliOption] public decimal CharacterNumber { get; init; } = 'a';
                [CliOption] public float Infinity { get; init; } = float.PositiveInfinity;
            }
            """;
        GeneratorResult generated = Generate(declaration);
        AssertNoErrors(generated);
        Type root = EmitAndLoad(generated.OutputCompilation).GetType("ConstantFixture.Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.AreEqual('x', model.Root.Options[0].DefaultValue);
        Assert.AreEqual("x", model.Root.Options[0].DefaultDisplay);
        Assert.AreEqual(0.1234567890123456789012345678m, model.Root.Options[1].DefaultValue);
        Assert.AreEqual("0.1234567890123456789012345678", model.Root.Options[1].DefaultDisplay);
        Assert.AreEqual(9007199254740992d, model.Root.Options[2].DefaultValue);
        Assert.AreEqual("9007199254740992", model.Root.Options[2].DefaultDisplay);
        Assert.AreEqual("5", model.Root.Options[3].DefaultDisplay);
        Assert.IsNull(model.Root.Options[4].DefaultValue);
        Assert.AreEqual("null", model.Root.Options[4].DefaultDisplay);
        Assert.AreEqual("null", model.Root.Options[5].DefaultDisplay);
        Assert.AreEqual(97m, model.Root.Options[6].DefaultValue);
        Assert.AreEqual("97", model.Root.Options[6].DefaultDisplay);
        Assert.AreEqual(float.PositiveInfinity, model.Root.Options[7].DefaultValue);
        Assert.AreEqual(ParseOutcome.Validated, new CliParser().Parse(model, []).Outcome);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-007")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_RejectsUnsupportedInitializersAndUnsafeRequiredMembers()
    {
        string[] declarations =
        [
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                [CliOption] public string Value { get; init; } = System.Guid.NewGuid().ToString();
            }
            """,
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                [CliOption] public bool Flag { get; init; } = true;
            }
            """,
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                [CliOption] public string Value { get; init; } = null!;
            }
            """,
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                public required string Unbound { get; init; }
            }
            """,
            """
            using Mauve.Cli.Declarations;
            public class Base { public required string Unbound { get; init; } }
            [CliCommand(IsRoot = true)] public partial class Root : Base { }
            """,
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                [CliOption] public required string Value { get; init; }
                [CliDirective("version")] private int Version() => 0;
            }
            """,
            """
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] [CliDirective<Directive>] public partial class Root { }
            [CliDirective] public partial class Directive {
                [CliOption(IsRequired = true)] public partial int Value { get; }
                public bool TryGetValue(out int value) { value = 0; return true; }
                [CliHandler] private int Run() => Value;
            }
            """
        ];
        foreach (string declaration in declarations)
        {
            GeneratorResult generated = Generate(declaration);
            Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
            Assert.DoesNotContain(static source => source.HintName.Contains("Root", StringComparison.Ordinal),
                generated.RunResult.Results.SelectMany(static result => result.GeneratedSources));
        }
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-007")]
    public void Generate_UpdatesInitializerAndRequiredAttributeEdits()
    {
        const string declaration = """
            using System.ComponentModel.DataAnnotations;
            using Mauve.Cli.Declarations;
            [CliCommand(IsRoot = true)] public partial class Root {
                [CliOption] public string Value { get; init; } = "first";
            }
            """;
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation input = CreateCompilation(CSharpSyntaxTree.ParseText(declaration, parseOptions, cancellationToken: TestContext.CancellationToken));
        GeneratorDriver driver = CreateDriver(parseOptions).RunGenerators(input, TestContext.CancellationToken);
        string[] initial = GetGeneratedSources(driver);
        driver = driver.RunGenerators(input, TestContext.CancellationToken);
        Assert.AreSequenceEqual(initial, GetGeneratedSources(driver));

        string edited = declaration.Replace("\"first\"", "\"second\"", StringComparison.Ordinal)
            .Replace("[CliOption]", "[Required] [CliOption]", StringComparison.Ordinal);
        CSharpCompilation changed = CreateCompilation(CSharpSyntaxTree.ParseText(edited, parseOptions, cancellationToken: TestContext.CancellationToken));
        driver = driver.RunGeneratorsAndUpdateCompilation(changed, out Compilation output, out _, TestContext.CancellationToken);
        Assert.IsEmpty(GetCompilerErrors(output));
        Assert.AreNotEqual(string.Join("\n", initial), string.Join("\n", GetGeneratedSources(driver)));
        Type root = EmitAndLoad(output).GetType("Root", throwOnError: true)!;
        CommandModel model = (CommandModel)root.GetMethod("CreateModel")!.Invoke(null, [null, false])!;
        Assert.IsTrue(model.Root.Options[0].IsRequired);
        Assert.AreEqual("second", model.Root.Options[0].DefaultValue);
    }

    private GeneratorResult Generate(params string[] sources)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, DocumentationMode.Parse);
        CSharpCompilation compilation = CreateCompilation(
            [.. sources.Select(source => CSharpSyntaxTree.ParseText(
                source, parseOptions, cancellationToken: TestContext.CancellationToken))]);
        GeneratorDriver driver = CreateDriver(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics,
            TestContext.CancellationToken);
        return new GeneratorResult(driver.GetRunResult(), output, diagnostics);
    }

    private static CSharpGeneratorDriver CreateDriver(CSharpParseOptions? parseOptions = null)
    {
        return CSharpGeneratorDriver.Create(
            [new CliModelGenerator().AsSourceGenerator()],
            parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.Preview));
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        return CSharpCompilation.Create(
            "GeneratorFixture",
            trees,
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference[] GetReferences()
    {
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return
        [
            .. trustedAssemblies.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(CommandModel).Assembly.Location))
        ];
    }

    private Compilation AddSource(Compilation compilation, string source)
    {
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            cancellationToken: TestContext.CancellationToken));
    }

    private Diagnostic[] GetCompilerErrors(Compilation compilation)
    {
        return [.. compilation.GetDiagnostics(TestContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }

    private Assembly EmitAndLoad(Compilation compilation)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emit = compilation.Emit(assemblyStream, cancellationToken: TestContext.CancellationToken);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(assemblyStream.ToArray());
    }

    private static string[] GetGeneratedSources(GeneratorDriver driver)
    {
        return [.. driver.GetRunResult().Results
            .SelectMany(static result => result.GeneratedSources)
            .OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .Select(static source => source.SourceText.ToString())];
    }

    private void AssertNoErrors(GeneratorResult generated)
    {
        Diagnostic[] generatorErrors = [.. generated.GeneratorDiagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Diagnostic[] compilationErrors = [.. generated.OutputCompilation.GetDiagnostics(TestContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.IsEmpty(generatorErrors, string.Join(Environment.NewLine, generatorErrors));
        Assert.IsEmpty(compilationErrors, string.Join(Environment.NewLine, compilationErrors));
    }

    private sealed record GeneratorResult(
        GeneratorDriverRunResult RunResult,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics);
}
