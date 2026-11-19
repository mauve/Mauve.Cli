using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
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
        SyntaxTree firstTree = CSharpSyntaxTree.ParseText(_validDeclaration, parseOptions);
        CSharpCompilation firstInput = CreateCompilation(firstTree);
        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _);
        string[] initialSources = GetGeneratedSources(driver);

        driver = driver.RunGeneratorsAndUpdateCompilation(firstInput, out _, out _);
        CollectionAssert.AreEqual(initialSources, GetGeneratedSources(driver));

        string changedDeclaration = _validDeclaration.Replace(
            "Aliases = [\"c\"]",
            "Aliases = [\"duplicate\"]",
            StringComparison.Ordinal);
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
            [CliPack(typeof(NetworkPack), "network", 0, Title = "Network")]
            public partial class Root
            {
                [CliHandler]
                private int Run(CommandInvocationContext context)
                {
                    OptionPackContext network = context.GetOptionPack("network");
                    OptionPackContext retry = network.GetNestedPack("retry");
                    if (!network.TryGetOptionValue(NetworkPack.Option0, out string? address)
                        || !retry.TryGetOptionValue(RetryPack.Option0, out int count))
                    {
                        throw new System.InvalidOperationException("The generated pack values are unavailable.");
                    }

                    return address!.Length + count;
                }
            }

            [CliOptionPack]
            [CliPack(typeof(RetryPack), "retry", 0, Title = "Retry")]
            [CliPackOption("--endpoint", typeof(string), OptionArity.RequiredScalar, 0, IsRequired = true)]
            public partial class NetworkPack
            {
            }

            [CliOptionPack]
            [CliPackOption("--retries", typeof(int), OptionArity.RequiredScalar, 0, IsRequired = true)]
            public partial class RetryPack
            {
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
        OptionDefinition generatedEndpoint = ReadPackOption(fixture, "GeneratorPackFixture.NetworkPack");
        OptionDefinition generatedRetries = ReadPackOption(fixture, "GeneratorPackFixture.RetryPack");
        InvocationResult generatedInvocation = await new CliInvoker().InvokeAsync(
            generatedModel,
            ["--endpoint=https://example.test", "--retries=2"]).ConfigureAwait(false);
        ParseResult generatedResult = generatedInvocation.ParseResult!;

        OptionDefinition endpoint = new("--endpoint", typeof(string), OptionArity.RequiredScalar, isRequired: true);
        OptionDefinition retries = new("--retries", typeof(int), OptionArity.RequiredScalar, isRequired: true);
        OptionPackBuilder retryPack = new OptionPackBuilder().AddOption(retries);
        OptionPackBuilder networkPack = new OptionPackBuilder()
            .AddOption(endpoint)
            .AddPack("retry", retryPack, "Retry");
        CommandModel explicitModel = CommandModel.Create(
            new CommandBuilder("fetch")
                .AddPack("network", networkPack, "Network")
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
            ["--endpoint=https://example.test", "--retries=2"]).ConfigureAwait(false);
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
        CollectionAssert.AreEqual(
            explicitResult.SelectedCommand!.Path.ToArray(),
            generatedResult.SelectedCommand!.Path.ToArray());
        CollectionAssert.AreEqual(
            HelpDocument.Create(explicitModel).OptionGroups.Select(static group => group.Title).ToArray(),
            HelpDocument.Create(generatedModel).OptionGroups.Select(static group => group.Title).ToArray());
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
            [CliPack(typeof(FirstPack), "first", 0)]
            [CliPack(typeof(SecondPack), "second", 1)]
            public partial class Root
            {
            }

            [CliOptionPack]
            [CliPackOption("--first", typeof(string), OptionArity.RequiredScalar, 0, IsRequired = true, Aliases = ["-x"])]
            public partial class FirstPack
            {
            }

            [CliOptionPack]
            [CliPackOption("--second", typeof(string), OptionArity.RequiredScalar, 0, IsRequired = true, Aliases = ["-x"])]
            public partial class SecondPack
            {
            }
            """;

        GeneratorResult generated = Generate(declaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
        Assert.IsFalse(generated.RunResult.Results.SelectMany(static result => result.GeneratedSources)
            .Any(static source => source.HintName.Contains("Root", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestProperty(MSTestMetadata.Feature, "FEAT-14")]
    [TestProperty(MSTestMetadata.Requirement, "FEAT-14-003")]
    public void Generate_RejectsPackCyclesWithoutRegisteringRoot()
    {
        const string declaration = """
            using Mauve.Cli.Declarations;

            [CliCommand("tool", IsRoot = true)]
            [CliPack(typeof(FirstPack), "first", 0)]
            public partial class Root
            {
            }

            [CliOptionPack]
            [CliPack(typeof(SecondPack), "second", 0)]
            public partial class FirstPack
            {
            }

            [CliOptionPack]
            [CliPack(typeof(FirstPack), "first", 0)]
            public partial class SecondPack
            {
            }
            """;

        GeneratorResult generated = Generate(declaration);
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

    private static CSharpGeneratorDriver CreateDriver(CSharpParseOptions? parseOptions = null)
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
        return
        [
            .. trustedAssemblies.Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path))
                .Append(MetadataReference.CreateFromFile(typeof(CommandModel).Assembly.Location))
        ];
    }

    private static Assembly EmitAndLoad(Compilation compilation)
    {
        using MemoryStream assemblyStream = new();
        EmitResult emit = compilation.Emit(assemblyStream);
        Assert.IsTrue(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(assemblyStream.ToArray());
    }

    private static OptionDefinition ReadPackOption(Assembly fixture, string packTypeName)
    {
        Type packType = fixture.GetType(packTypeName, throwOnError: true)!;
        return (OptionDefinition)packType.GetField("Option0", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
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
        Diagnostic[] generatorErrors = [.. generated.GeneratorDiagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Diagnostic[] compilationErrors = [.. generated.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
        Assert.IsFalse(generatorErrors.Length > 0, string.Join(Environment.NewLine, generatorErrors));
        Assert.IsFalse(compilationErrors.Length > 0, string.Join(Environment.NewLine, compilationErrors));
    }

    private sealed record GeneratorResult(
        GeneratorDriverRunResult RunResult,
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics);
}
