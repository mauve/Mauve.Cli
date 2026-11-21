using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
            public partial int Count { get; }

            [CliArgument]
            public partial string File { get; }

            [CliHandler]
            private int Run(CancellationToken cancellationToken) => Count + File.Length;
        }
        """;

    public required TestContext TestContext { get; set; }

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
                public partial IReadOnlyList<int> ItemIds { get; }

                [CliOption]
                public partial bool DryRun { get; }

                [CliArgument]
                public partial string InputFile { get; }

                [CliArgument(MinimumValues = 0)]
                public partial IReadOnlyList<string> ExtraPaths { get; }
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
                public partial string FirstTreeFirst { get; }

                [CliOption(IsRequired = true)]
                public partial string FirstTreeSecond { get; }
            }
            """;
        const string second = """
            using Mauve.Cli.Declarations;

            namespace PartialOrderFixture;

            public partial class RootCommand
            {
                [CliOption(IsRequired = true)]
                public partial string SecondTree { get; }
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
                public partial IReadOnlyList<int> Values { get; }

                [CliArgument(MinimumValues = 0, MaximumValues = -1)]
                public partial IReadOnlyList<string> Paths { get; }
            }
            """;
        const string invalidDeclaration = """
            using Mauve.Cli.Declarations;
            using Mauve.Cli.Definitions;

            [CliCommand(IsRoot = true)]
            public partial class ToolCommand
            {
                [CliOption(Arity = OptionArity.BooleanFlag, IsRequired = true)]
                public partial string Value { get; }
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
            [CliPack<NetworkPack>("network", Title = "Network")]
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
            [CliPack<RetryPack>("retry", Title = "Retry")]
            [CliPackOption<string>("--endpoint", IsRequired = true)]
            public partial class NetworkPack
            {
            }

            [CliOptionPack]
            [CliPackOption<int>("--retries", IsRequired = true)]
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
            ["--endpoint=https://example.test", "--retries=2"], TestContext.CancellationToken).ConfigureAwait(false);
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
            [CliPack<FirstPack>("first")]
            [CliPack<SecondPack>("second")]
            public partial class Root
            {
            }

            [CliOptionPack]
            [CliPackOption<string>("--first", IsRequired = true, Aliases = ["-x"])]
            public partial class FirstPack
            {
            }

            [CliOptionPack]
            [CliPackOption<string>("--second", IsRequired = true, Aliases = ["-x"])]
            public partial class SecondPack
            {
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
            [CliPack<FirstPack>("first")]
            public partial class Root
            {
            }

            [CliOptionPack]
            [CliPack<SecondPack>("second")]
            public partial class FirstPack
            {
            }

            [CliOptionPack]
            [CliPack<FirstPack>("first")]
            public partial class SecondPack
            {
            }
            """;

        GeneratorResult generated = Generate(declaration);
        Assert.IsTrue(generated.GeneratorDiagnostics.Any(static diagnostic => diagnostic.Id == "MCLI003"));
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

    private GeneratorResult Generate(params string[] sources)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, DocumentationMode.None);
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
