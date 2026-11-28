using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Mauve.Cli.Completion;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Parsing;

[TestClass]
public sealed class ConverterRegistrationTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_UsesDefinitionModelAndHostConvertersWithoutMutatingSnapshots()
    {
        OptionDefinition ordinary = new("--ordinary", typeof(Code), OptionArity.RequiredScalar);
        OptionDefinition special = new("--special", typeof(Code), OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create(static (_, _) => ValueConversion.FromValue(new Code(99))));
        OptionDefinition host = new("--host", typeof(Guid), OptionArity.RequiredScalar);
        PositionalDefinition codes = new("codes", typeof(IReadOnlyList<Code>), maximumValues: null,
            elementType: typeof(Code), converter: ValueConverterRegistration.Create<Code, CodeConverter>());
        CommandBuilder builder = new CommandBuilder("tool")
            .RegisterConverter<Code, CodeConverter>()
            .AddOption(ordinary).AddOption(special).AddOption(host).AddPositional(codes);
        CommandModel model = CommandModel.Create(builder);
        builder.RegisterConverter(static (_, _) => ValueConversion.FromValue(Guid.Empty));
        ConverterRegistry hostConverters = new ConverterRegistryBuilder()
            .Register(static (_, _) => ValueConversion.FromValue(Guid.Empty)).Build();
        ParseResult result = new CliParser(hostConverters).Parse(model,
            ["--ordinary=12", "--special=ignored", "--host=ignored", "23", "34"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(ordinary, out Code ordinaryValue));
        Assert.IsTrue(result.TryGetOptionValue(special, out Code specialValue));
        Assert.IsTrue(result.TryGetOptionValue(host, out Guid hostValue));
        Assert.AreEqual(new Code(12), ordinaryValue);
        Assert.AreEqual(new Code(99), specialValue);
        Assert.AreEqual(Guid.Empty, hostValue);
        Assert.IsTrue(result.TryGetPositionalValue(model.Root, codes, out IReadOnlyList<Code>? values));
        Assert.AreSequenceEqual([new Code(23), new Code(34)], values);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    [SuppressMessage("Usage", "CA1849:Call async methods when in an async method", Justification = "This test compares synchronous and asynchronous host entry points.")]
    public async Task Run_UsesHostConfigurationAndRejectsConflictingRegistriesBeforeCallbacks()
    {
        int calls = 0;
        OptionDefinition option = new("--code", typeof(Code), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(option)
            .SetHandler((context, _) => context.TryGetOptionValue(option, out Code value) ? value.Value : -1));
        ConverterRegistry converters = new ConverterRegistryBuilder().Register<Code, CodeConverter>().Build();
        using StringWriter output = new();
        using StringWriter errors = new();
        CliHostOptions options = new(output, errors, converters: converters);
        Assert.AreEqual(12, CliInvoker.Run(model, ["--code=12"], options, TestContext.CancellationToken));
        Assert.AreEqual(23, await CliInvoker.RunAsync(model, ["--code=23"], options, TestContext.CancellationToken).ConfigureAwait(false));

        CommandModel conflict = CommandModel.Create(new CommandBuilder("tool")
            .RegisterConverter((_, _) =>
            {
                calls++;
                return ValueConversion.FromValue(new Code(1));
            }).AddOption(option));
        Assert.ThrowsExactly<InvalidOperationException>(() => new CliParser(converters).Parse(conflict, ["--code=1"]));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new CompletionService(converters).CompleteAsync(
                new CompletionRequest(conflict, [], 0, 0), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_RejectsUnknownEnumNamesAndNumericAliases()
    {
        OptionDefinition mode = new("--mode", typeof(Mode), OptionArity.RequiredScalar);
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum([new KeyValuePair<string, Mode>("Slow", Mode.Slow), new KeyValuePair<string, Mode>("Fast", Mode.Fast)])
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddConverters(converters).AddOption(mode));
        ParseResult valid = new CliParser().Parse(model, ["--mode=Fast"]);

        Assert.AreEqual(ParseOutcome.Validated, valid.Outcome);
        Assert.IsTrue(valid.TryGetOptionValue(mode, out Mode value));
        Assert.AreEqual(Mode.Fast, value);

        foreach (string input in new[] { "fast", "1", "Unknown" })
        {
            ParseResult invalid = new CliParser().Parse(model, ["--mode=" + input]);
            Assert.AreEqual(ParseOutcome.Invalid, invalid.Outcome, input);
            Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalid.Diagnostics[0].Code);
            Assert.AreEqual("enum.unknown", invalid.Diagnostics[0].FailureCode);
            Assert.IsFalse(invalid.Diagnostics[0].Message.Contains(input, StringComparison.Ordinal));
            Assert.IsEmpty(invalid.Values);
        }
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_ReportsRegisteredFailuresSafelyWithStableCoordinates()
    {
        const string secret = "sentinel-secret-value";
        const string failureCode = "code.rejected";
        OptionDefinition option = new("--secret", typeof(Code), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .RegisterConverter((_, _) => ValueConversion.Failure<Code>(failureCode))
            .AddOption(option));
        string[] arguments = ["--secret=" + secret];

        ParseResult first = new CliParser().Parse(model, arguments);
        ParseResult repeated = new CliParser().Parse(model, arguments);

        Assert.AreEqual(ParseOutcome.Invalid, first.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, first.Diagnostics[0].Code);
        Assert.AreEqual(failureCode, first.Diagnostics[0].FailureCode);
        Assert.AreEqual(0, first.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, first.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(9, first.Diagnostics[0].ValueOffset);
        Assert.AreEqual("--secret", first.Diagnostics[0].DefinitionName);
        Assert.AreSequenceEqual(["tool"], first.Diagnostics[0].DefinitionPath);
        Assert.IsFalse(first.Diagnostics[0].Message.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(first.Diagnostics[0].ToString().Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(string.Join(Environment.NewLine, first.FormatDiagnostics()).Contains(secret, StringComparison.Ordinal));
        Assert.IsEmpty(first.Values);

        Assert.AreEqual(first.Outcome, repeated.Outcome);
        Assert.AreEqual(first.Diagnostics[0].Code, repeated.Diagnostics[0].Code);
        Assert.AreEqual(first.Diagnostics[0].FailureCode, repeated.Diagnostics[0].FailureCode);
        Assert.AreEqual(first.Diagnostics[0].TokenIndex, repeated.Diagnostics[0].TokenIndex);
        Assert.AreEqual(first.Diagnostics[0].ValueTokenIndex, repeated.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(first.Diagnostics[0].ValueOffset, repeated.Diagnostics[0].ValueOffset);
        Assert.AreEqual(first.Diagnostics[0].DefinitionName, repeated.Diagnostics[0].DefinitionName);
        Assert.AreSequenceEqual(first.Diagnostics[0].DefinitionPath, repeated.Diagnostics[0].DefinitionPath);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Create_RejectsMismatchedConvertersAndChildRegistrations()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--code", typeof(Code), OptionArity.RequiredScalar,
                converter: ValueConverterRegistration.Create(static (_, _) => ValueConversion.FromValue(1))))));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder("tool")
            .AddChild(new CommandBuilder("child").RegisterConverter<Code, CodeConverter>())));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--flag", typeof(bool), OptionArity.BooleanFlag,
                converter: ValueConverterRegistration.Create(static (_, _) => ValueConversion.FromValue(true))))));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_UnionCasesUseFirstSuccessAndSupportNestedNullableCollections()
    {
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterUnion<NumberOrText>(union => union.Case<int>(static value => new(value)).Case<string>(static value => new(value)))
            .RegisterUnion<NestedChoice>(union => union.Case<NumberOrText>(static value => new(value)).Case<Guid>(static value => new(value)))
            .RegisterNullable<NestedChoice>()
            .Build();
        OptionDefinition choice = new("--choice", typeof(IReadOnlyList<NestedChoice?>),
            OptionArity.RepeatedSingleCollection, elementType: typeof(NestedChoice?));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddConverters(converters).AddOption(choice));
        ParseResult result = new CliParser().Parse(model, ["--choice", "-12", "--choice=hello"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(choice, out IReadOnlyList<NestedChoice?>? values));
        Assert.HasCount(2, values!);
        Assert.AreEqual(-12, ((NumberOrText)values![0]!.Value.Value!).Value);
        Assert.AreEqual("hello", ((NumberOrText)values[1]!.Value.Value!).Value);

        CommandModel noMatch = CommandModel.Create(new CommandBuilder("tool")
            .RegisterUnion<CodeOrDate>(union => union.Case<Code>(static value => new(value)).Case<DateOnly>(static value => new(value)))
            .RegisterConverter<Code, CodeConverter>()
            .AddOption(new OptionDefinition("--choice", typeof(CodeOrDate), OptionArity.RequiredScalar)));
        ParseResult invalid = new CliParser().Parse(noMatch, ["--choice=secret-invalid-value"]);
        Assert.AreEqual(ParseOutcome.Invalid, invalid.Outcome);
        Assert.AreEqual("union.no-match", invalid.Diagnostics[0].FailureCode);
        Assert.IsEmpty(invalid.Values);
        Assert.IsFalse(invalid.Diagnostics[0].Message.Contains("secret-invalid-value", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_UnionFaultsPropagateAndInvalidDependenciesAreRejected()
    {
        int nextCaseCalls = 0;
        CommandModel fault = CommandModel.Create(new CommandBuilder("tool")
            .RegisterUnion<NumberOrText>(union => union.Case<int>(static value => new(value)).Case<string>(static value => new(value)))
            .RegisterConverter<int>(static (_, _) => throw new InvalidOperationException("callback fault"))
            .RegisterConverter((value, _) =>
            {
                nextCaseCalls++;
                return ValueConversion.FromValue(value);
            }).AddOption(new OptionDefinition("--value", typeof(NumberOrText), OptionArity.RequiredScalar)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new CliParser().Parse(fault, ["--value=12"]));
        Assert.AreEqual(0, nextCaseCalls);
        Assert.ThrowsExactly<InvalidOperationException>(() => new ConverterRegistryBuilder().RegisterUnion<NumberOrText>(static _ => { }));
        Assert.ThrowsExactly<ArgumentException>(() => new ConverterRegistryBuilder().RegisterUnion<NumberOrText>(
            union => union.Case<int>(static value => new(value)).Case<int>(static value => new(value))));
        CommandModel missing = CommandModel.Create(new CommandBuilder("tool")
            .RegisterUnion<CodeOrDate>(union => union.Case<Code>(static value => new(value)).Case<DateOnly>(static value => new(value)))
            .AddOption(new OptionDefinition("--value", typeof(CodeOrDate), OptionArity.RequiredScalar)));
        Assert.AreEqual(ParseDiagnosticCode.InvalidModel, new CliParser().Parse(missing, ["--value=12"]).Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-15")]
    [TestProperty(Metadata.Requirement, "FEAT-15-004")]
    public async Task Invoke_UsesDefinitionConvertersForDirectiveValues()
    {
        DirectiveValueDefinition value = new("code", typeof(Code), converter: ValueConverterRegistration.Create<Code, CodeConverter>());
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddDirective(new DirectiveBuilder("code").AddValue(value)
                .SetHandler((context, _) => context.TryGetValue(value, out Code code)
                    ? DirectiveResult.StopProcessing(code.Value) : throw new InvalidOperationException("Missing code."))));
        InvocationResult result = await new CliInvoker().InvokeAsync(model, ["[code:42]"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(42, result.ExitCode);
        InvocationResult invalid = await new CliInvoker().InvokeAsync(model, ["[code:bad]"], TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.InvalidInput, invalid.Outcome);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-003")]
    public async Task Complete_UsesOnlyApprovedDefinitionConverters()
    {
        int calls = 0;
        OptionDefinition code = new("--code", typeof(Code), OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create((_, _) =>
            {
                calls++;
                return ValueConversion.FromValue(new Code(42));
            }, completionSafe: true));
        OptionDefinition target = new("--target", typeof(string), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(code).AddOption(target));
        CompletionProviderRegistry providers = new CompletionProviderRegistryBuilder()
            .Register(target, (context, _) => Candidates(context.TryGetOptionValue(code, out Code value)
                ? value.Value.ToString(CultureInfo.InvariantCulture) : "missing"), CompletionDependency.ForOption(code)).Build();
        CompletionResult result = await new CompletionService().CompleteAsync(
            new CompletionRequest(model, ["--code=ignored", "--target="], 1, 9), providers, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, calls);
        Assert.AreEqual("42", result.Candidates[0].InsertionText);

        int unsafeCalls = 0;
        OptionDefinition unsafeCode = new("--code", typeof(Code), OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create((_, _) =>
            {
                unsafeCalls++;
                return ValueConversion.FromValue(new Code(99));
            }));
        CommandModel unsafeModel = CommandModel.Create(new CommandBuilder("tool")
            .RegisterConverter<Code, CodeConverter>(completionSafe: true).AddOption(unsafeCode).AddOption(target));
        CompletionProviderRegistry unsafeProviders = new CompletionProviderRegistryBuilder()
            .Register(target, (context, _) => Candidates(context.TryGetOptionValue(unsafeCode, out Code value)
                ? value.Value.ToString(CultureInfo.InvariantCulture) : "missing"), CompletionDependency.ForOption(unsafeCode)).Build();
        CompletionResult unavailable = await new CompletionService().CompleteAsync(
            new CompletionRequest(unsafeModel, ["--code=ignored", "--target="], 1, 9), unsafeProviders, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(0, unsafeCalls);
        Assert.AreEqual("missing", unavailable.Candidates[0].InsertionText);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-10")]
    [TestProperty(Metadata.Requirement, "FEAT-10-001")]
    public async Task Complete_DoesNotUseEnumNamesFromAnOverriddenConverter()
    {
        OptionDefinition local = new("--local", typeof(Mode), OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create(static (_, _) => ValueConversion.FromValue(Mode.Fast)));
        OptionDefinition nullable = new("--nullable", typeof(Mode?), OptionArity.RequiredScalar, allowsNull: true);
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum([new KeyValuePair<string, Mode>("Slow", Mode.Slow), new KeyValuePair<string, Mode>("Fast", Mode.Fast)])
            .Register(static (_, _) => ValueConversion.FromValue<Mode?>(Mode.Fast)).Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddConverters(converters).AddOption(local).AddOption(nullable));
        foreach (string active in new[] { "--local=", "--nullable=" })
        {
            CompletionResult result = await new CompletionService().CompleteAsync(
                new CompletionRequest(model, [active], 0, active.Length), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
            Assert.AreEqual(CompletionOutcome.Completed, result.Outcome);
            Assert.IsEmpty(result.Candidates);
        }
    }

    private static async IAsyncEnumerable<CompletionValue> Candidates(string value)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new CompletionValue(value);
    }

    private readonly record struct Code(int Value);

    private enum Mode
    {
        Slow,
        Fast
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The converter is used through its static generic provider contract.")]
    private sealed class CodeConverter : ICliValueConverter<Code>
    {
        public static ValueConversion<Code> Convert(string value, ConversionContext context)
            => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int code)
                ? ValueConversion.FromValue(new Code(code)) : ValueConversion.Failure<Code>("code.invalid");
    }

#if NET11_0_OR_GREATER
    private readonly union NumberOrText(int, string);

    private readonly union NestedChoice(NumberOrText, Guid);

    private readonly union CodeOrDate(Code, DateOnly);
#endif
}
