using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Mauve.Cli.Definitions;
using Mauve.Cli.Generators;
using Mauve.Cli.Invocation;
using MSTestMetadata = Mauve.Governance.Testing.MSTest.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Generators.Tests;

[TestClass]
public sealed class CliModelGeneratorTests
{
    private const string ValidDeclaration = """
        using Mauve.Cli.Declarations;
        using Mauve.Cli.Definitions;
        using Mauve.Cli.Invocation;
        using System.Threading;

        namespace GeneratorFixture;

        [CliCommand("tool", IsRoot = true)]
        [CliChild(typeof(Child), 0)]
        public partial class Root
        {
        }

        [CliCommand("copy", Aliases = ["c"])]
        public partial class Child
        {
            [CliOption("--count", OptionArity.RequiredScalar, 0, IsRequired = true)]
            public partial int Count { get; }

            [CliArgument("file", 0)]
            public partial string File { get; }

            [CliHandler]
            private int Run(CancellationToken cancellationToken) => Count + File.Length;
        }
        """;

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-001")]
    public async Task Generate_CompilesAndRunsNestedAliasBinding()
    {
        GeneratorResult generated = Generate(ValidDeclaration);
        AssertNoErrors(generated);

        Assembly fixture = EmitAndLoad(generated.OutputCompilation);
        Type rootType = fixture.GetType("GeneratorFixture.Root", throwOnError: true)!;
        CommandModel generatedModel = (CommandModel)rootType.GetMethod("CreateModel")!
            .Invoke(null, [null, false])!;
        InvocationResult generatedResult = await new CliInvoker()
            .InvokeAsync(generatedModel, ["c", "--count=3", "file.txt"])
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
            .InvokeAsync(explicitModel, ["c", "--count=3", "file.txt"])
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, generatedResult.Outcome);
        Assert.AreEqual(explicitResult.Outcome, generatedResult.Outcome);
        Assert.AreEqual(explicitResult.ExitCode, generatedResult.ExitCode);
        Assert.AreEqual("tool copy", string.Join(" ", generatedResult.ParseResult!.SelectedCommand!.Path));
        Assert.AreEqual(11, generatedResult.ExitCode);
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-002")]
    public void Generate_UpdatesAliasesAndProducesStableUnchangedOutput()
    {
        GeneratorDriver driver = CreateDriver();
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        SyntaxTree firstTree = CSharpSyntaxTree.ParseText(ValidDeclaration, parseOptions);
        CSharpCompilation firstInput = CreateCompilation(firstTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _);
        string[] initialSources = GetGeneratedSources(driver);

        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _);
        CollectionAssert.AreEqual(initialSources, GetGeneratedSources(driver));

        string changedDeclaration = ValidDeclaration.Replace("Aliases = [\"c\"]", "Aliases = [\"duplicate\"]", StringComparison.Ordinal);
        CSharpCompilation changedInput = CreateCompilation(CSharpSyntaxTree.ParseText(changedDeclaration, parseOptions));
        driver = driver.RunGeneratorsAndUpdateCompilation(changedInput, out Compilation changedOutput, out ImmutableArray<Diagnostic> diagnostics);
        Assert.IsFalse(diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsFalse(changedOutput.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsTrue(GetGeneratedSources(driver).Any(static source => source.Contains("duplicate", StringComparison.Ordinal)));
        Assert.AreNotEqual(
            string.Join(Environment.NewLine, initialSources),
            string.Join(Environment.NewLine, GetGeneratedSources(driver)));
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
                [CliOption("--count", OptionArity.RequiredScalar, 0, IsRequired = true)]
                public int Count { get; }
            }
            """;

        GeneratorResult generated = Generate(invalidDeclaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI002"));
        Assert.IsFalse(generated.RunResult.Results.SelectMany(static result => result.GeneratedSources)
            .Any(static source => source.HintName.Contains("Root", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-11")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-11-004")]
    public void Generate_ReportsDuplicateSiblingAliasesAndEmitsNoRoot()
    {
        const string invalidDeclaration = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            [CliChild(typeof(First), 0)]
            [CliChild(typeof(Second), 1)]
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
        Assert.IsFalse(generated.RunResult.Results.SelectMany(static result => result.GeneratedSources)
            .Any(static source => source.HintName.Contains("Root", StringComparison.Ordinal)));
    }

    private static GeneratorResult Generate(string source)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, DocumentationMode.None);
        CSharpCompilation compilation = CreateCompilation(CSharpSyntaxTree.ParseText(source, parseOptions));
        GeneratorDriver driver = CreateDriver(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);
        return new GeneratorResult(driver.GetRunResult(), output, diagnostics);
    }

    private static GeneratorDriver CreateDriver(CSharpParseOptions? parseOptions = null)
    {
        return CSharpGeneratorDriver.Create(
            [new CliModelGenerator().AsSourceGenerator()],
            parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.Preview));
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree)
    {
        return CSharpCompilation.Create(
            "GeneratorFixture",
            [tree],
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference[] GetReferences()
    {
        string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedAssemblies.Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(CommandModel).Assembly.Location))
            .ToArray();
    }

    private static Assembly EmitAndLoad(Compilation compilation)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emit = compilation.Emit(assemblyStream);
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

    private static void AssertNoErrors(GeneratorResult generated)
    {
        Assert.IsFalse(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsFalse(generated.OutputCompilation.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private sealed record GeneratorResult(
        GeneratorDriverRunResult RunResult,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics);
}
