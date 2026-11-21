using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Completion;

[TestClass]
public sealed class CompletionServiceTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task CompleteAsync_SuggestsVisibleCommandsAndOptionsWithCursorRanges()
    {
        OptionDefinition mode = new("--mode", typeof(string), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool")
                .AddOption(mode)
                .AddChild(new CommandBuilder("file").AddAlias("f"))
                .AddChild(new CommandBuilder("find") { IsHidden = true }));
        CompletionService service = new();

        CompletionResult commands = await service.CompleteAsync(
            new CompletionRequest(model, ["fi"], 0, 2), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult options = await service.CompleteAsync(
            new CompletionRequest(model, ["--mode=fast"], 0, 4), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(CompletionOutcome.Completed, commands.Outcome);
        Assert.AreEqual("file", commands.Candidates.Single().InsertionText);
        Assert.AreEqual(CompletionCandidateKind.Command, commands.Candidates[0].Kind);
        Assert.AreEqual(0, commands.Candidates[0].ReplacementStart);
        Assert.AreEqual(2, commands.Candidates[0].ReplacementLength);
        Assert.AreEqual(CompletionOutcome.Completed, options.Outcome);
        Assert.AreEqual("--mode", options.Candidates.Single().InsertionText);
        Assert.AreEqual(0, options.Candidates[0].ReplacementStart);
        Assert.AreEqual(6, options.Candidates[0].ReplacementLength);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task CompleteAsync_PreservesOmittedAndExplicitEmptyDescriptions()
    {
        CommandModel model = CommandModel.Create(
            new CommandBuilder("tool")
                .AddOption(new OptionDefinition(
                    "--omitted",
                    typeof(string),
                    OptionArity.RequiredScalar))
                .AddOption(new OptionDefinition(
                    "--empty",
                    typeof(string),
                    OptionArity.RequiredScalar,
                    description: string.Empty)));
        CompletionService service = new();

        CompletionResult omitted = await service.CompleteAsync(
            new CompletionRequest(model, ["--o"], 0, 3), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult empty = await service.CompleteAsync(
            new CompletionRequest(model, ["--e"], 0, 3), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNull(omitted.Candidates.Single().Description);
        Assert.AreEqual(string.Empty, empty.Candidates.Single().Description);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-002")]
    public async Task CompleteAsync_ProvidesBoundedValuesFromDeclaredDependencies()
    {
        OptionDefinition tenant = new("--tenant", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition region = new("--region", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition resource = new("--resource", typeof(string), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(tenant)
            .AddOption(region)
            .AddOption(resource));
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(
                resource,
                (context, cancellationToken) => SuggestResources(context, tenant, region, cancellationToken),
                CompletionDependency.ForOption(tenant))
            .Build();
        string active = "--resource=al";
        CompletionResult result = await new CompletionService().CompleteAsync(
            new CompletionRequest(
                model,
                ["--tenant=acme", "--region=west", active],
                2,
                active.IndexOf("al", StringComparison.Ordinal) + 1),
            providers, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(CompletionOutcome.Completed, result.Outcome);
        Assert.HasCount(1, result.Candidates);
        Assert.AreEqual("alpha item", result.Candidates[0].InsertionText);
        Assert.AreEqual("tenant:acme;region:unavailable", result.Candidates[0].Description);
        Assert.AreEqual("--resource=".Length, result.Candidates[0].ReplacementStart);
        Assert.AreEqual(2, result.Candidates[0].ReplacementLength);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task CompleteAsync_UsesNegativeNumericPrefixesAndNegatingAliasesAsValues()
    {
        OptionDefinition count = new("--count", typeof(int), OptionArity.RequiredScalar);
        OptionDefinition verbose = new(
            "--verbose",
            typeof(bool),
            OptionArity.BooleanFlag,
            aliases: [new OptionAlias("--no-verbose", OptionAliasKind.Negating)]);
        OptionDefinition target = new("--target", typeof(string), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(count)
            .AddOption(verbose)
            .AddOption(target));
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(target, (context, _) => SingleValue(
                context.TryGetOptionValue(verbose, out bool? value) ? value!.Value.ToString() : "missing"),
                CompletionDependency.ForOption(verbose))
            .Build();
        CompletionService service = new();

        CompletionResult numeric = await service.CompleteAsync(
            new CompletionRequest(model, ["--count", "-3"], 1, 2),
            new CompletionProviderRegistryBuilder()
                .Register(count, static (_, _) => SingleValue("-3"))
                .Build(), TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult negated = await service.CompleteAsync(
            new CompletionRequest(model, ["--no-verbose", "--target="], 1, "--target=".Length),
            providers, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("-3", numeric.Candidates.Single().InsertionText);
        Assert.AreEqual("False", negated.Candidates.Single().InsertionText);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task CompleteAsync_UsesExplicitEnumMapsWithoutReflection()
    {
        OptionDefinition mode = new("--mode", typeof(Format), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(mode));
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum([
                new KeyValuePair<string, Format>("plain", Format.Plain),
                new KeyValuePair<string, Format>("pretty", Format.Pretty)
            ])
            .Build();
        string active = "--mode=pr";
        CompletionResult result = await new CompletionService(converters).CompleteAsync(
            new CompletionRequest(model, [active], 0, active.Length), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult ordered = await new CompletionService(converters).CompleteAsync(
            new CompletionRequest(model, ["--mode="], 0, "--mode=".Length), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(CompletionOutcome.Completed, result.Outcome);
        Assert.AreEqual("pretty", result.Candidates.Single().InsertionText);
        Assert.AreSequenceEqual(
            ["plain", "pretty"], [.. ordered.Candidates.Select(static candidate => candidate.InsertionText)]);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task CompleteAsync_RespectsPendingOptionalBooleanAndEndMarkerStates()
    {
        int optionalCalls = 0;
        OptionDefinition required = new("--required", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition optional = new(
            "--filter",
            typeof(string),
            OptionArity.OptionalScalar,
            bareValuePolicy: BareValuePolicy.Null,
            allowsNull: true);
        OptionDefinition flag = new("--verbose", typeof(bool), OptionArity.BooleanFlag);
        PositionalDefinition paths = new("paths", typeof(IReadOnlyList<string>), minimumValues: 0,
            maximumValues: null, elementType: typeof(string));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(required)
            .AddOption(optional)
            .AddOption(flag)
            .AddPositional(paths));
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(required, static (_, _) => SingleValue("required-value"))
            .Register(optional, (_, _) =>
            {
                optionalCalls++;
                return SingleValue("optional-value");
            })
            .Register(paths, static (_, _) => SingleValue("-name.txt"))
            .Build();
        CompletionService service = new();

        CompletionResult requiredPending = await service.CompleteAsync(
            new CompletionRequest(model, ["--required", "req"], 1, 3), providers, TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult optionalBare = await service.CompleteAsync(
            new CompletionRequest(model, ["--filter", "x"], 1, 1), providers, TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult boolean = await service.CompleteAsync(
            new CompletionRequest(model, ["--verbose", "tr"], 1, 2), providers, TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult positional = await service.CompleteAsync(
            new CompletionRequest(model, ["--", "-na"], 1, 3), providers, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("required-value", requiredPending.Candidates.Single().InsertionText);
        Assert.AreEqual(CompletionOutcome.Completed, optionalBare.Outcome);
        Assert.AreEqual(0, optionalCalls);
        Assert.AreEqual("true", boolean.Candidates.Single().InsertionText);
        Assert.AreEqual(CompletionCandidateKind.Positional, positional.Candidates.Single().Kind);
        Assert.AreEqual("-name.txt", positional.Candidates.Single().InsertionText);
        Assert.AreEqual(0, positional.Candidates[0].ReplacementStart);
        Assert.AreEqual(3, positional.Candidates[0].ReplacementLength);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-003")]
    public async Task CompleteAsync_ReportsProviderFaultsAndBoundsCandidateWork()
    {
        OptionDefinition target = new("--target", typeof(string), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(target));
        CompletionProviderRegistry failing = new CompletionProviderRegistryBuilder()
            .Register(target, static (_, _) => ThrowingValues())
            .Build();
        CompletionProviderRegistry excessive = new CompletionProviderRegistryBuilder()
            .Register(target, static (_, _) => ManyValues())
            .Build();
        CompletionService service = new();
        string active = "--target=";

        CompletionResult failed = await service.CompleteAsync(
            new CompletionRequest(model, [active], 0, active.Length), failing, TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult limited = await service.CompleteAsync(
            new CompletionRequest(model, [active], 0, active.Length), excessive, TestContext.CancellationToken).ConfigureAwait(false);
        OptionDefinition longDescription = new(
            "--large",
            typeof(bool),
            OptionArity.BooleanFlag,
            description: new string('x', 1020));
        CommandModel oversizedModel = CommandModel.Create(
            new CommandBuilder("tool").AddOption(longDescription));
        CompletionResult oversized = await service.CompleteAsync(
            new CompletionRequest(oversizedModel, ["--la"], 0, 4), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(CompletionOutcome.ProviderFailed, failed.Outcome);
        Assert.IsFalse(failed.IsComplete);
        Assert.HasCount(1, failed.Candidates);
        Assert.AreEqual(CompletionOutcome.LimitReached, limited.Outcome);
        Assert.HasCount(128, limited.Candidates);
        Assert.IsFalse(limited.IsComplete);
        Assert.AreEqual(CompletionOutcome.LimitReached, oversized.Outcome);
        Assert.IsEmpty(oversized.Candidates);
        Assert.IsFalse(oversized.IsComplete);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-003")]
    [TestProperty(Metadata.Requirement, "FEAT-10-004")]
    public async Task CompleteAsync_CancellationAndInvalidContextRemainExplicit()
    {
        OptionDefinition target = new("--target", typeof(string), OptionArity.RequiredScalar);
        OptionDefinition secret = new("--secret", typeof(Format), OptionArity.RequiredScalar, isSensitive: true);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(target).AddOption(secret));
        int providerCalls = 0;
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(target, (_, token) =>
            {
                providerCalls++;
                return WaitingValues(token);
            })
            .Build();
        using CancellationTokenSource cancellation = new();
        string active = "--target=";
        ValueTask<CompletionResult> pending = new CompletionService().CompleteAsync(
            new CompletionRequest(model, [active], 0, active.Length), providers, cancellation.Token);
        await cancellation.CancelAsync().ConfigureAwait(false);
        CompletionResult cancelled = await pending.ConfigureAwait(false);
        using CancellationTokenSource preCancellation = new();
        await preCancellation.CancelAsync().ConfigureAwait(false);
        CompletionResult preCancelled = await new CompletionService().CompleteAsync(
            new CompletionRequest(model, [active], 0, active.Length),
            providers,
            preCancellation.Token).ConfigureAwait(false);
        CompletionService service = new();
        CompletionResult malformed = await service.CompleteAsync(
            new CompletionRequest(model, ["--unknown", string.Empty], 1, 0), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        ConverterRegistry enumConverters = new ConverterRegistryBuilder()
            .RegisterEnum([
                new KeyValuePair<string, Format>("plain", Format.Plain),
                new KeyValuePair<string, Format>("pretty", Format.Pretty)
            ])
            .Build();
        CompletionResult secretValues = await new CompletionService(enumConverters).CompleteAsync(
            new CompletionRequest(model, ["--secret=pr"], 0, "--secret=pr".Length), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult unsupportedShell = await CompletionService.CompleteForShellAsync(
            "bash",
            new CompletionRequest(model, [], 0, 0), TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(CompletionOutcome.Cancelled, cancelled.Outcome);
        Assert.AreEqual(CompletionOutcome.Cancelled, preCancelled.Outcome);
        Assert.AreEqual(1, providerCalls);
        Assert.AreEqual(CompletionOutcome.ContextFailed, malformed.Outcome);
        Assert.IsFalse(malformed.IsComplete);
        Assert.AreEqual(CompletionOutcome.Completed, secretValues.Outcome);
        Assert.IsEmpty(secretValues.Candidates);
        Assert.AreEqual(CompletionOutcome.UnsupportedShell, unsupportedShell.Outcome);
        _ = Assert.Throws<ArgumentException>(() =>
            new CompletionProviderRegistryBuilder().Register(
                target,
                static (_, _) => EmptyValues(),
                CompletionDependency.ForOption(secret)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-005")]
    public async Task CompleteAsync_SuggestsDirectivesBeforeTheFirstCommand()
    {
        DirectiveBuilder debug = new DirectiveBuilder("debug")
            .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        DirectiveBuilder format = new DirectiveBuilder("format")
            .AddValue(new DirectiveValueDefinition("format", typeof(Format)))
            .SetHandler(static (_, _) => DirectiveResult.ContinueProcessing);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(debug)
            .AddDirective(format)
            .AddChild(new CommandBuilder("copy")));
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum([
                new KeyValuePair<string, Format>("plain", Format.Plain),
                new KeyValuePair<string, Format>("pretty", Format.Pretty)
            ])
            .Build();
        CompletionService service = new(converters);

        CompletionResult names = await service.CompleteAsync(
            new CompletionRequest(model, ["[de"], 0, 3),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult values = await service.CompleteAsync(
            new CompletionRequest(model, ["[format:pre"], 0, 11),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        CompletionResult afterCommand = await service.CompleteAsync(
            new CompletionRequest(model, ["copy", "[de"], 1, 3),
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual("[debug]", names.Candidates.Single().InsertionText);
        Assert.AreEqual(CompletionCandidateKind.Directive, names.Candidates[0].Kind);
        Assert.AreEqual("[format:pretty]", values.Candidates.Single().InsertionText);
        Assert.AreEqual(CompletionCandidateKind.Value, values.Candidates[0].Kind);
        Assert.IsEmpty(afterCommand.Candidates);
    }

    private static async IAsyncEnumerable<CompletionValue> SuggestResources(
        CompletionContext context,
        OptionDefinition tenant,
        OptionDefinition region,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string label = context.TryGetOptionValue(tenant, out string? value) ? value! : "missing";
        string regionLabel = context.TryGetOptionValue(region, out string? regionValue)
            ? regionValue!
            : "unavailable";
        yield return new CompletionValue("alpha item", description: $"tenant:{label};region:{regionLabel}");
        yield return new CompletionValue("beta", description: $"tenant:{label};region:{regionLabel}");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<CompletionValue> ThrowingValues()
    {
        yield return new CompletionValue("first");
        await Task.Yield();
        throw new InvalidOperationException("private provider details");
    }

    private static async IAsyncEnumerable<CompletionValue> ManyValues()
    {
        for (int index = 0; index < 600; index++)
        {
            yield return new CompletionValue($"item-{index:D3}");
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<CompletionValue> WaitingValues(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        yield return new CompletionValue("unreachable");
    }

    private static async IAsyncEnumerable<CompletionValue> EmptyValues()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static async IAsyncEnumerable<CompletionValue> SingleValue(string value)
    {
        yield return new CompletionValue(value);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private enum Format
    {
        Plain,
        Pretty
    }
}
