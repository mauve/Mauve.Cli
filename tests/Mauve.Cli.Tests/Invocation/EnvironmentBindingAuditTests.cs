using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Invocation;
using Mauve.Cli.Parsing;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Invocation;

[TestClass]
public sealed class EnvironmentBindingAuditTests
{
    public required TestContext TestContext { get; set; }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-002")]
    public async Task InvokeAsync_ExplicitCollectionReplacesEnvironmentAndSkipsLookup()
    {
        OptionDefinition items = new(
            "--items",
            typeof(IReadOnlyList<string>),
            OptionArity.GreedyCollection,
            elementType: typeof(string));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(items));
        List<string> lookups = [];
        CliHostOptions options = new(
            enableEnvironmentVariables: true,
            environmentVariableLookup: name =>
            {
                lookups.Add(name);
                return "environment-one,environment-two";
            });

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model,
            ["--items", "argv-one", "argv-two"],
            options,
            TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.NoHandler, result.Outcome);
        Assert.IsTrue(result.ParseResult!.TryGetOptionValue(items, out IReadOnlyList<string>? values));
        Assert.AreSequenceEqual(["argv-one", "argv-two"], values!);
        ParsedValue entry = result.ParseResult.Values.Single(value => ReferenceEquals(value.Option, items));
        Assert.AreEqual(ValueOrigin.Explicit, entry.Origin);
        Assert.AreEqual(ValuePresence.WithValue, entry.Presence);
        Assert.IsEmpty(lookups);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-003")]
    public async Task InvokeAsync_UsesCustomConvertersForEnvironmentScalarsAndCollectionElements()
    {
        List<string> scalarInputs = [];
        List<string> elementInputs = [];
        OptionDefinition scalar = new(
            "--scalar",
            typeof(AuditScalarValue),
            OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create((value, _) =>
            {
                scalarInputs.Add(value);
                return ValueConversion.FromValue(new AuditScalarValue(string.Concat("converted:", value)));
            }));
        OptionDefinition items = new(
            "--items",
            typeof(IReadOnlyList<AuditElementValue>),
            OptionArity.GreedyCollection,
            elementType: typeof(AuditElementValue));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .RegisterConverter((value, _) =>
            {
                elementInputs.Add(value);
                return ValueConversion.FromValue(new AuditElementValue(value.ToUpperInvariant()));
            })
            .AddOption(scalar)
            .AddOption(items));
        CliHostOptions options = new(
            enableEnvironmentVariables: true,
            environmentVariableLookup: name => name switch
            {
                "TOOL_SCALAR" => "scalar-input",
                "TOOL_ITEMS" => "first,second",
                _ => null
            });

        InvocationResult result = await new CliInvoker().InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.NoHandler, result.Outcome);
        Assert.IsTrue(result.ParseResult!.TryGetOptionValue(scalar, out AuditScalarValue scalarValue));
        Assert.AreEqual(new AuditScalarValue("converted:scalar-input"), scalarValue);
        Assert.IsTrue(result.ParseResult.TryGetOptionValue(items, out IReadOnlyList<AuditElementValue>? itemValues));
        Assert.AreSequenceEqual(
            [new AuditElementValue("FIRST"), new AuditElementValue("SECOND")],
            itemValues!);
        Assert.AreSequenceEqual(["scalar-input"], scalarInputs);
        Assert.AreSequenceEqual(["first", "second"], elementInputs);
        Assert.IsTrue(result.ParseResult.Values
            .Where(value => ReferenceEquals(value.Option, scalar) || ReferenceEquals(value.Option, items))
            .All(static value => value.Origin == ValueOrigin.Environment && value.Presence == ValuePresence.WithValue));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-003")]
    public async Task InvokeAsync_UsesHostConverterForEnvironmentValues()
    {
        OptionDefinition value = new("value", typeof(AuditScalarValue), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool").AddOption(value));
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register(static (text, _) => ValueConversion.FromValue(new AuditScalarValue("host:" + text)))
            .Build();
        CliHostOptions options = new(converters: converters, enableEnvironmentVariables: true,
            environmentVariableLookup: static name => name == "TOOL_VALUE" ? "input" : null);

        InvocationResult result = await new CliInvoker().InvokeAsync(model, [], options, TestContext.CancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.NoHandler, result.Outcome);
        Assert.IsTrue(result.ParseResult!.TryGetOptionValue(value, out AuditScalarValue converted));
        Assert.AreEqual(new AuditScalarValue("host:input"), converted);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-003")]
    public async Task InvokeAsync_UsesExplicitNumericCultureAndValidatesEnvironmentValues()
    {
        int crossValidationCalls = 0;
        int handlerCalls = 0;
        OptionDefinition minimum = new(
            "--minimum",
            typeof(decimal),
            OptionArity.RequiredScalar,
            validators:
            [
                new ValueValidator<decimal>(static (value, _) => value >= 0m
                    ? ValueValidationOutcome.Success
                    : ValueValidationOutcome.Failure("minimum.invalid"))
            ]);
        OptionDefinition maximum = new(
            "--maximum",
            typeof(decimal),
            OptionArity.RequiredScalar,
            validators:
            [
                new ValueValidator<decimal>(static (value, _) => value >= 0m
                    ? ValueValidationOutcome.Success
                    : ValueValidationOutcome.Failure("maximum.invalid"))
            ]);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(minimum)
            .AddOption(maximum)
            .AddCrossValidator(context =>
            {
                crossValidationCalls++;
                return context.TryGetOptionValue(minimum, out decimal lower)
                    && context.TryGetOptionValue(maximum, out decimal upper)
                    && lower <= upper
                        ? ValueValidationOutcome.Success
                        : ValueValidationOutcome.Failure("bounds.invalid");
            })
            .SetHandler((_, _) => handlerCalls++));
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["TOOL_MINIMUM"] = "1,5",
            ["TOOL_MAXIMUM"] = "2,5"
        };
        CliHostOptions options = new(
            numericCulture: CultureInfo.GetCultureInfo("fr-FR"),
            enableEnvironmentVariables: true,
            environmentVariableLookup: name => environment.TryGetValue(name, out string? value) ? value : null);
        CliInvoker invoker = new();

        InvocationResult accepted = await invoker.InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.Completed, accepted.Outcome,
            string.Join("; ", accepted.ParseResult!.FormatDiagnostics(includeArgumentText: false)));
        Assert.IsTrue(accepted.ParseResult!.TryGetOptionValue(minimum, out decimal parsedMinimum));
        Assert.AreEqual(1.5m, parsedMinimum);
        Assert.IsTrue(accepted.ParseResult.TryGetOptionValue(maximum, out decimal parsedMaximum));
        Assert.AreEqual(2.5m, parsedMaximum);
        Assert.AreEqual(1, crossValidationCalls);
        Assert.AreEqual(1, handlerCalls);

        environment["TOOL_MINIMUM"] = "3,5";
        environment["TOOL_MAXIMUM"] = "2,5";
        InvocationResult crossRejected = await invoker.InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, crossRejected.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, crossRejected.ParseResult!.Diagnostics[0].Code);
        Assert.AreEqual("bounds.invalid", crossRejected.ParseResult.Diagnostics[0].FailureCode);
        Assert.IsNull(crossRejected.ParseResult.Diagnostics[0].TokenIndex);
        Assert.IsNull(crossRejected.ParseResult.Diagnostics[0].ValueTokenIndex);
        Assert.IsNull(crossRejected.ParseResult.Diagnostics[0].ValueOffset);
        Assert.IsFalse(crossRejected.ParseResult.Diagnostics[0].ToString().Contains("3,5", StringComparison.Ordinal));
        Assert.AreEqual(2, crossValidationCalls);
        Assert.AreEqual(1, handlerCalls);

        environment["TOOL_MINIMUM"] = "-1,5";
        InvocationResult valueRejected = await invoker.InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.AreEqual(InvocationOutcome.InvalidInput, valueRejected.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, valueRejected.ParseResult!.Diagnostics[0].Code);
        Assert.AreEqual("minimum.invalid", valueRejected.ParseResult.Diagnostics[0].FailureCode);
        Assert.IsNull(valueRejected.ParseResult.Diagnostics[0].TokenIndex);
        Assert.AreEqual(2, crossValidationCalls);
        Assert.AreEqual(1, handlerCalls);

        environment["TOOL_MINIMUM"] = "1,5";
        InvocationResult explicitParser = await new CliInvoker(new CliParser()).InvokeAsync(
            model, [], options, TestContext.CancellationToken).ConfigureAwait(false);
        Assert.AreEqual(InvocationOutcome.InvalidInput, explicitParser.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, explicitParser.ParseResult!.Diagnostics[0].Code);
        Assert.AreEqual(1, handlerCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-16")]
    [TestProperty(Metadata.Requirement, "FEAT-16-003")]
    public async Task InvokeAsync_PropagatesEnvironmentConverterAndValidatorFaultsWithoutRendering()
    {
        InvalidOperationException converterFault = new("private converter fault");
        OptionDefinition converted = new(
            "--converted",
            typeof(AuditFaultValue),
            OptionArity.RequiredScalar,
            converter: ValueConverterRegistration.Create<AuditFaultValue>((_, _) => throw converterFault));
        CommandModel converterModel = CommandModel.Create(new CommandBuilder("tool").AddOption(converted));
        using StringWriter converterOutput = new();
        using StringWriter converterErrors = new();
        CliHostOptions converterOptions = new(
            standardOutput: converterOutput,
            standardError: converterErrors,
            enableEnvironmentVariables: true,
            environmentVariableLookup: static _ => "private-converter-input");

        InvalidOperationException actualConverterFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new CliInvoker().InvokeAsync(
                converterModel, [], converterOptions, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreSame(converterFault, actualConverterFault);
        Assert.AreEqual(string.Empty, converterOutput.ToString());
        Assert.AreEqual(string.Empty, converterErrors.ToString());

        InvalidOperationException validatorFault = new("private validator fault");
        OptionDefinition validated = new(
            "--validated",
            typeof(string),
            OptionArity.RequiredScalar,
            validators:
            [
                new ValueValidator<string>((_, _) => throw validatorFault)
            ]);
        CommandModel validatorModel = CommandModel.Create(new CommandBuilder("tool").AddOption(validated));
        using StringWriter validatorOutput = new();
        using StringWriter validatorErrors = new();
        CliHostOptions validatorOptions = new(
            standardOutput: validatorOutput,
            standardError: validatorErrors,
            enableEnvironmentVariables: true,
            environmentVariableLookup: static _ => "private-validator-input");

        InvalidOperationException actualValidatorFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new CliInvoker().InvokeAsync(
                validatorModel, [], validatorOptions, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreSame(validatorFault, actualValidatorFault);
        Assert.AreEqual(string.Empty, validatorOutput.ToString());
        Assert.AreEqual(string.Empty, validatorErrors.ToString());

        InvalidOperationException crossValidatorFault = new("private cross-validator fault");
        CommandModel crossValidatorModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--value", typeof(string), OptionArity.RequiredScalar))
            .AddCrossValidator(_ => throw crossValidatorFault));
        using StringWriter crossValidatorOutput = new();
        using StringWriter crossValidatorErrors = new();
        CliHostOptions crossValidatorOptions = new(
            standardOutput: crossValidatorOutput,
            standardError: crossValidatorErrors,
            enableEnvironmentVariables: true,
            environmentVariableLookup: static _ => "private-cross-validator-input");

        InvalidOperationException actualCrossValidatorFault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await new CliInvoker().InvokeAsync(
                crossValidatorModel, [], crossValidatorOptions, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

        Assert.AreSame(crossValidatorFault, actualCrossValidatorFault);
        Assert.AreEqual(string.Empty, crossValidatorOutput.ToString());
        Assert.AreEqual(string.Empty, crossValidatorErrors.ToString());
    }

    private readonly record struct AuditScalarValue(string Value);

    private readonly record struct AuditElementValue(string Value);

    private readonly record struct AuditFaultValue(string Value);
}
