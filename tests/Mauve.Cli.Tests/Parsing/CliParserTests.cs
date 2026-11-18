using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Mauve.Cli.Conversion;
using Mauve.Cli.Definitions;
using Mauve.Cli.Parsing;
using Mauve.Cli.Validation;
using Mauve.Governance.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Mauve.Cli.Tests.Parsing;

[TestClass]
public sealed class CliParserTests
{
    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-001")]
    public void Parse_PreservesArgumentBoundariesAndEmptyRestValues()
    {
        CommandModel model = CreateLeafModel();
        string[] arguments = ["--", "-file", "a b", string.Empty];

        ParseResult result = new CliParser().Parse(model, arguments);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "-file", "a b", string.Empty }, paths!.ToArray());
        Assert.AreEqual(arguments.Length, result.Arguments.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-002")]
    public void Parse_ReportsMissingValuesWithoutConsumingTheEndMarker()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar))
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));

        ParseResult result = new CliParser().Parse(model, ["--count", "--", "--file"]);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, result.Diagnostics[0].Code);
        Assert.AreEqual(0, result.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, result.Values.Count);
        Assert.AreEqual(0, result.UnmatchedTokens.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-03")]
    [TestProperty(Metadata.Requirement, "FEAT-03-004")]
    public void Parse_TreatsDashTokensAsOptionsUntilTheEndMarker()
    {
        CommandModel model = CreateLeafModel();

        ParseResult result = new CliParser().Parse(model, ["--", "-file"]);
        ParseResult unknown = new CliParser().Parse(model, ["--wat"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "-file" }, paths!.ToArray());
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknown.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-001")]
    public void Parse_ConvertsBuiltInsAndExplicitEnumMaps()
    {
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .RegisterEnum(
            [
                new KeyValuePair<string, AccessMode>("Read", AccessMode.Read),
                new KeyValuePair<string, AccessMode>("Write", AccessMode.Write)
            ])
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--count=42", "--mode=Read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out int count));
        Assert.AreEqual(42, count);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[1], out AccessMode mode));
        Assert.AreEqual(AccessMode.Read, mode);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-002")]
    public void Parse_UsesInvariantNumbersByDefaultAndSelectedDecimalSeparator()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--ratio", typeof(double), OptionArity.RequiredScalar)));
        CliParser invariantParser = new();
        CliParser commaParser = new(numericCulture: CultureInfo.GetCultureInfo("fr-FR"));

        ParseResult invariant = invariantParser.Parse(model, ["--ratio=1.5"]);
        ParseResult comma = commaParser.Parse(model, ["--ratio=1,5"]);
        ParseResult ambientMismatch = invariantParser.Parse(model, ["--ratio=1,5"]);

        Assert.AreEqual(ParseOutcome.Validated, invariant.Outcome);
        Assert.AreEqual(ParseOutcome.Validated, comma.Outcome);
        Assert.AreEqual(ParseOutcome.Invalid, ambientMismatch.Outcome);
        Assert.IsTrue(invariant.TryGetOptionValue(model.Root, model.Root.Options[0], out double invariantValue));
        Assert.IsTrue(comma.TryGetOptionValue(model.Root, model.Root.Options[0], out double commaValue));
        Assert.AreEqual(1.5d, invariantValue);
        Assert.AreEqual(1.5d, commaValue);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_UsesRegisteredConvertersAndRejectsDuplicateRegistrations()
    {
        ConverterRegistryBuilder builder = new ConverterRegistryBuilder()
            .Register((value, _) => value == "read"
                ? ValueConversion.FromValue(AccessMode.Read)
                : ValueConversion.Failure<AccessMode>("mode.unknown"));
        ConverterRegistry converters = builder.Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--mode=read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.ThrowsExactly<InvalidOperationException>(() => builder.Register(
            static (_, _) => ValueConversion.FromValue(AccessMode.Write)));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_RunsConvertersInInputTokenOrder()
    {
        List<string> callOrder = [];
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register((value, _) =>
            {
                callOrder.Add(value);
                return value == "Read"
                    ? ValueConversion.FromValue(AccessMode.Read)
                    : ValueConversion.FromValue(AccessMode.Write);
            })
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--first", typeof(AccessMode), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--second", typeof(AccessMode), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser(converters).Parse(model, ["--second=Write", "--first=Read"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        CollectionAssert.AreEqual(new[] { "Write", "Read" }, callOrder);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-004")]
    public void Parse_ReportsSafeConversionFailuresWithoutPartialValues()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser().Parse(model, ["--count=2147483648"]);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, result.Diagnostics[0].Code);
        Assert.AreEqual(0, result.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, result.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(8, result.Diagnostics[0].ValueOffset);
        Assert.IsFalse(result.Diagnostics[0].Message.Contains("2147483648", StringComparison.Ordinal));
        Assert.AreEqual(0, result.Values.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-04")]
    [TestProperty(Metadata.Requirement, "FEAT-04-003")]
    public void Parse_PropagatesConverterFaults()
    {
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register<AccessMode>(static (_, _) => throw new InvalidOperationException("converter fault"))
            .Build();
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--mode", typeof(AccessMode), OptionArity.RequiredScalar)));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser(converters).Parse(model, ["--mode=read"]));

        Assert.AreEqual("converter fault", exception.Message);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-001")]
    public void Parse_HandlesBooleanFormsAndExplicitNegation()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--verbose",
                typeof(bool),
                OptionArity.BooleanFlag,
                aliases: [new OptionAlias("--no-verbose", OptionAliasKind.Negating)])));

        ParseResult enabled = new CliParser().Parse(model, ["--verbose"]);
        ParseResult disabled = new CliParser().Parse(model, ["--no-verbose"]);
        ParseResult spaceFalse = new CliParser().Parse(model, ["--verbose", "false"]);
        ParseResult invalid = new CliParser().Parse(model, ["--verbose=maybe"]);
        ParseResult repeated = new CliParser().Parse(model, ["--verbose", "--no-verbose"]);

        Assert.IsTrue(enabled.TryGetOptionValue(model.Root, model.Root.Options[0], out bool trueValue));
        Assert.IsTrue(trueValue);
        Assert.IsTrue(disabled.TryGetOptionValue(model.Root, model.Root.Options[0], out bool falseValue));
        Assert.IsFalse(falseValue);
        Assert.IsTrue(spaceFalse.TryGetOptionValue(model.Root, model.Root.Options[0], out bool spaceFalseValue));
        Assert.IsFalse(spaceFalseValue);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalid.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, repeated.Diagnostics[0].Code);

        CommandBuilder collidingNames = new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--verbose",
                typeof(bool),
                OptionArity.BooleanFlag,
                aliases: [new OptionAlias("--no-verbose", OptionAliasKind.Negating)]))
            .AddOption(new OptionDefinition("--no-verbose", typeof(bool), OptionArity.BooleanFlag));
        Assert.ThrowsExactly<ArgumentException>(() => CommandModel.Create(collidingNames));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-002")]
    public void Parse_LeavesFollowingTokensForPositionalBindingAfterBareOptional()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--filter",
                typeof(string),
                OptionArity.OptionalScalar,
                allowsNull: true,
                bareValuePolicy: BareValuePolicy.Null))
            .AddPositional(new PositionalDefinition("name", typeof(string))));

        ParseResult result = new CliParser().Parse(model, ["--filter", "child"]);

        Assert.AreEqual(ParseOutcome.Validated, result.Outcome);
        Assert.IsTrue(result.TryGetOptionValue(model.Root, model.Root.Options[0], out string? filter));
        Assert.IsNull(filter);
        Assert.IsTrue(result.TryGetPositionalValue(model.Root, model.Root.Positionals[0], out string? name));
        Assert.AreEqual("child", name);
        Assert.AreEqual(ValuePresence.Bare, result.Values[0].Presence);
        Assert.AreEqual(ValueOrigin.Explicit, result.Values[0].Origin);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-003")]
    public void Parse_AccumulatesCollectionOccurrencesAndRejectsScalarDuplicates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--tag",
                typeof(IReadOnlyList<string>),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(string)))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));

        ParseResult collection = new CliParser().Parse(model, ["--tag=a", "--tag=b"]);
        ParseResult duplicate = new CliParser().Parse(model, ["--count=1", "--count=2"]);

        Assert.IsTrue(collection.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? tags));
        CollectionAssert.AreEqual(new[] { "a", "b" }, tags!.ToArray());
        IList<string> readOnlyTags = (IList<string>)tags!;
        Assert.IsTrue(readOnlyTags.IsReadOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => readOnlyTags[0] = "changed");
        Assert.AreEqual("a", tags![0]);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, duplicate.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-003")]
    public void Parse_ConsumesGreedyCollectionsUntilTheNextOptionBoundary()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--tag",
                typeof(IReadOnlyList<string>),
                OptionArity.GreedyCollection,
                elementType: typeof(string)))
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));

        ParseResult values = new CliParser().Parse(model, ["--tag", "a", "b", "--", "-file"]);
        ParseResult missing = new CliParser().Parse(model, ["--tag", "--"]);

        Assert.IsTrue(values.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? tags));
        CollectionAssert.AreEqual(new[] { "a", "b" }, tags!.ToArray());
        Assert.IsTrue(values.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "-file" }, paths!.ToArray());
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missing.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-05")]
    [TestProperty(Metadata.Requirement, "FEAT-05-004")]
    public void Parse_BindsRestValuesAndRejectsShortClusters()
    {
        CommandModel model = CreateLeafModel();

        ParseResult rest = new CliParser().Parse(model, ["--", "a", "-b", "c"]);
        ParseResult cluster = new CliParser().Parse(model, ["-ab"]);

        Assert.IsTrue(rest.TryGetPositionalValue(
            model.Root,
            model.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "a", "-b", "c" }, paths!.ToArray());
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, cluster.Diagnostics[0].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-001")]
    public void Parse_DistinguishesExplicitAndDefaultValuesAndKeepsRequiredDefaultsInert()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 0)));
        int factoryCalls = 0;
        CommandModel required = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--required",
                typeof(int),
                OptionArity.RequiredScalar,
                isRequired: true,
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    return 4;
                })));

        ParseResult defaulted = new CliParser().Parse(model, []);
        ParseResult explicitResult = new CliParser().Parse(model, ["--count=0"]);
        ParseResult missing = new CliParser().Parse(required, []);

        Assert.IsTrue(defaulted.TryGetOptionValue(model.Root, model.Root.Options[0], out int defaultValue));
        Assert.IsTrue(explicitResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int explicitValue));
        Assert.AreEqual(0, defaultValue);
        Assert.AreEqual(0, explicitValue);
        Assert.AreEqual(ValueOrigin.Default, defaulted.Values[0].Origin);
        Assert.AreEqual(ValueOrigin.Explicit, explicitResult.Values[0].Origin);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, missing.Diagnostics[0].Code);
        Assert.AreEqual(0, factoryCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-002")]
    public void Parse_InvokesAbsentFactoriesOncePerParseAndNotAfterInvalidInput()
    {
        int factoryCalls = 0;
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--items",
                typeof(IReadOnlyList<string>),
                OptionArity.RepeatedSingleCollection,
                elementType: typeof(string),
                defaultValuePolicy: DefaultValuePolicy.Factory,
                defaultValueFactory: () =>
                {
                    factoryCalls++;
                    return new List<string>();
                }))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        CliParser parser = new();

        ParseResult first = parser.Parse(model, []);
        ParseResult second = parser.Parse(model, []);
        ParseResult invalid = parser.Parse(model, ["--count=bad"]);

        Assert.AreEqual(2, factoryCalls);
        Assert.IsTrue(first.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? firstItems));
        Assert.IsTrue(second.TryGetOptionValue(
            model.Root,
            model.Root.Options[0],
            out IReadOnlyList<string>? secondItems));
        Assert.AreNotSame(firstItems, secondItems);
        Assert.AreEqual(ParseOutcome.Invalid, invalid.Outcome);
        Assert.AreEqual(2, factoryCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-003")]
    public void Parse_RunsValueValidatorsOnAvailableValuesAndSkipsFailedConversions()
    {
        int validationCalls = 0;
        ValueValidator<int> validator = new((value, _) =>
        {
            validationCalls++;
            return value <= 10
                ? ValueValidationOutcome.Success
                : ValueValidationOutcome.Failure("range.invalid");
        });
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 20,
                validators: [validator])));
        CliParser parser = new();

        ParseResult rejectedDefault = parser.Parse(model, []);
        int callsAfterDefault = validationCalls;
        ParseResult invalidConversion = parser.Parse(model, ["--count=bad"]);
        int callsAfterInvalidConversion = validationCalls;
        ParseResult valid = parser.Parse(model, ["--count=10"]);

        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, rejectedDefault.Diagnostics[0].Code);
        Assert.AreEqual(1, callsAfterDefault);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidConversion.Diagnostics[0].Code);
        Assert.AreEqual(1, callsAfterInvalidConversion);
        Assert.AreEqual(ParseOutcome.Validated, valid.Outcome);
        Assert.AreEqual(2, validationCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-004")]
    public void Parse_RunsCrossValidationOnlyAfterValueValidation()
    {
        int crossCalls = 0;
        ValueValidator<bool> leftValidator = new((value, _) => value
            ? ValueValidationOutcome.Failure("value.invalid")
            : ValueValidationOutcome.Success);
        OptionDefinition left = new("--left", typeof(bool), OptionArity.RequiredScalar, validators: [leftValidator]);
        OptionDefinition right = new("--right", typeof(bool), OptionArity.RequiredScalar);
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(left)
            .AddOption(right)
            .AddCrossValidator(context =>
            {
                crossCalls++;
                return context.WasProvided(left) && context.WasProvided(right)
                    ? ValueValidationOutcome.Failure("exclusive.conflict")
                    : ValueValidationOutcome.Success;
            }));
        CliParser parser = new();

        ParseResult conflict = parser.Parse(model, ["--left=false", "--right=false"]);
        ParseResult invalidValue = parser.Parse(model, ["--left=maybe"]);
        ParseResult valueRejected = parser.Parse(model, ["--left=true"]);
        ParseResult single = parser.Parse(model, ["--left=false"]);

        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, conflict.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidValue.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.ValidationFailed, valueRejected.Diagnostics[0].Code);
        Assert.AreEqual(ParseOutcome.Validated, single.Outcome);
        Assert.AreEqual(2, crossCalls);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-003")]
    public void Parse_PropagatesValidatorFaults()
    {
        ValueValidator<int> validator = new(static (_, _) => throw new InvalidOperationException("validator fault"));
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                validators: [validator])));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser().Parse(model, ["--count=1"]));

        Assert.AreEqual("validator fault", exception.Message);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-06")]
    [TestProperty(Metadata.Requirement, "FEAT-06-004")]
    public void Parse_PropagatesCrossValidatorFaults()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddCrossValidator(static _ => throw new InvalidOperationException("cross-validator fault")));

        InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => new CliParser().Parse(model, []));

        Assert.AreEqual("cross-validator fault", exception.Message);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-001")]
    public void Parse_PreservesProvenanceAndIsolatesResultsAcrossParses()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--count",
                typeof(int),
                OptionArity.RequiredScalar,
                defaultValuePolicy: DefaultValuePolicy.Constant,
                defaultValue: 0)));
        CliParser parser = new();
        string[] arguments = ["--count=0"];

        ParseResult explicitResult = parser.Parse(model, arguments);
        arguments[0] = "--count=8";
        ParseResult defaultResult = parser.Parse(model, []);

        Assert.AreEqual("--count=0", explicitResult.Arguments[0]);
        Assert.IsTrue(explicitResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int explicitValue));
        Assert.IsTrue(defaultResult.TryGetOptionValue(model.Root, model.Root.Options[0], out int defaultValue));
        Assert.AreEqual(0, explicitValue);
        Assert.AreEqual(0, defaultValue);
        Assert.AreEqual(ValueOrigin.Explicit, explicitResult.Values[0].Origin);
        Assert.AreEqual(ValueOrigin.Default, defaultResult.Values[0].Origin);
        Assert.AreNotSame(explicitResult.Values, defaultResult.Values);

        ParseResult? firstConcurrent = null;
        ParseResult? secondConcurrent = null;
        Parallel.Invoke(
            () => firstConcurrent = parser.Parse(model, ["--count=1"]),
            () => secondConcurrent = parser.Parse(model, ["--count=2"]));
        Assert.IsTrue(firstConcurrent!.TryGetOptionValue(model.Root, model.Root.Options[0], out int firstValue));
        Assert.IsTrue(secondConcurrent!.TryGetOptionValue(model.Root, model.Root.Options[0], out int secondValue));
        Assert.AreEqual(1, firstValue);
        Assert.AreEqual(2, secondValue);

        CommandModel otherModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        Assert.ThrowsExactly<ArgumentException>(() => explicitResult.TryGetOptionValue<int>(
            otherModel.Root,
            otherModel.Root.Options[0],
            out _));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_ReportsStableCategoriesAndCoordinates()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        CliParser parser = new();

        ParseResult invalidValue = parser.Parse(model, ["--count=bad"]);
        ParseResult missingValue = parser.Parse(model, ["--count"]);
        ParseResult repeatedInvalidValue = parser.Parse(model, ["--count=bad"]);

        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, invalidValue.Diagnostics[0].Code);
        Assert.AreEqual(0, invalidValue.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, invalidValue.Diagnostics[0].ValueTokenIndex);
        Assert.AreEqual(8, invalidValue.Diagnostics[0].ValueOffset);
        Assert.IsFalse(invalidValue.Diagnostics[0].Message.Contains("bad", StringComparison.Ordinal));
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missingValue.Diagnostics[0].Code);
        Assert.AreEqual(invalidValue.Diagnostics[0].Code, repeatedInvalidValue.Diagnostics[0].Code);
        Assert.AreEqual(invalidValue.Diagnostics[0].TokenIndex, repeatedInvalidValue.Diagnostics[0].TokenIndex);

        CommandModel missingAndInvalid = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition(
                "--required",
                typeof(string),
                OptionArity.RequiredScalar,
                isRequired: true))
            .AddOption(new OptionDefinition("--count", typeof(int), OptionArity.RequiredScalar)));
        ParseResult ordered = parser.Parse(missingAndInvalid, ["--count=bad"]);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, ordered.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, ordered.Diagnostics[1].Code);
        Assert.AreEqual(0, ordered.Diagnostics[0].ValueTokenIndex);
        Assert.IsNull(ordered.Diagnostics[1].TokenIndex);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_ReportsUnsupportedModelsWithoutSelectingACommand()
    {
        CommandModel model = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--date", typeof(DateTime), OptionArity.RequiredScalar)));

        ParseResult result = new CliParser().Parse(model, []);

        Assert.AreEqual(ParseOutcome.Invalid, result.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.InvalidModel, result.Diagnostics[0].Code);
        Assert.AreEqual(0, result.SelectedPath.Count);
        Assert.IsNull(result.Diagnostics[0].TokenIndex);
        Assert.AreEqual("--date", result.Diagnostics[0].DefinitionName);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-002")]
    public void Parse_EnforcesConfiguredInputAndDiagnosticLimits()
    {
        CommandModel countLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumArgumentCount: 1));
        ParseResult tooManyArguments = new CliParser().Parse(countLimitedModel, ["one", "two"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, tooManyArguments.Diagnostics[0].Code);
        Assert.AreEqual(1, tooManyArguments.Diagnostics[0].TokenIndex);
        Assert.AreEqual(0, tooManyArguments.SelectedPath.Count);

        CommandModel tokenLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTokenLength: 3));
        ParseResult longToken = new CliParser().Parse(tokenLimitedModel, ["long"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, longToken.Diagnostics[0].Code);
        Assert.AreEqual(0, longToken.Diagnostics[0].TokenIndex);

        CommandModel totalLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumTotalCodeUnits: 3));
        ParseResult longVector = new CliParser().Parse(totalLimitedModel, ["ab", "cd"]);
        Assert.AreEqual(ParseDiagnosticCode.InputLimitExceeded, longVector.Diagnostics[0].Code);
        Assert.AreEqual(1, longVector.Diagnostics[0].TokenIndex);

        CommandModel diagnosticLimitedModel = CommandModel.Create(
            new CommandBuilder("tool"),
            new CliLimits(maximumDiagnostics: 2));
        ParseResult manyErrors = new CliParser().Parse(diagnosticLimitedModel, ["--bad", "--worse", "--worst"]);
        Assert.AreEqual(2, manyErrors.Diagnostics.Count);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, manyErrors.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DiagnosticLimitExceeded, manyErrors.Diagnostics[1].Code);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-003")]
    public void Parse_FailsOnUnknownAndExcessInputButExposesItForInspection()
    {
        CommandModel optionModel = CommandModel.Create(new CommandBuilder("tool")
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar)));
        ParseResult unknownOption = new CliParser().Parse(optionModel, ["--wat"]);
        Assert.AreEqual(ParseOutcome.Invalid, unknownOption.Outcome);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknownOption.Diagnostics[0].Code);
        Assert.AreEqual("--wat", unknownOption.UnmatchedTokens[0].Value);
        Assert.IsFalse(unknownOption.Diagnostics[0].Message.Contains("--wat", StringComparison.Ordinal));

        CommandModel positionalModel = CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition("name", typeof(string))));
        ParseResult excess = new CliParser().Parse(positionalModel, ["first", "secret"]);
        Assert.AreEqual(ParseDiagnosticCode.UnexpectedArgument, excess.Diagnostics[0].Code);
        Assert.AreEqual("secret", excess.UnmatchedTokens[0].Value);
        Assert.IsFalse(excess.Diagnostics[0].Message.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-07")]
    [TestProperty(Metadata.Requirement, "FEAT-07-004")]
    public void Parse_SuggestsBoundedVisibleCanonicalNames()
    {
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy").AddAlias("cp"))
            .AddChild(new CommandBuilder("hidden") { IsHidden = true })
            .AddOption(new OptionDefinition("--output", typeof(string), OptionArity.RequiredScalar))
            .AddOption(new OptionDefinition("--secret", typeof(string), OptionArity.RequiredScalar, isHidden: true));

        ParseResult commandTypo = new CliParser().Parse(CommandModel.Create(root), ["copi"]);
        ParseResult optionTypo = new CliParser().Parse(CommandModel.Create(root), ["--ouptut"]);
        ParseResult hiddenTypo = new CliParser().Parse(CommandModel.Create(root), ["--secrett"]);
        string longName = string.Concat("--", new string('x', 130));
        ParseResult overlongTypo = new CliParser().Parse(CommandModel.Create(root), [longName]);

        CollectionAssert.AreEqual(new[] { "copy" }, commandTypo.Diagnostics[0].Suggestions.ToArray());
        CollectionAssert.AreEqual(new[] { "--output" }, optionTypo.Diagnostics[0].Suggestions.ToArray());
        Assert.AreEqual(0, hiddenTypo.Diagnostics[0].Suggestions.Count);
        Assert.AreEqual(0, overlongTypo.Diagnostics[0].Suggestions.Count);

        CommandBuilder options = new("tool");
        for (int index = 0; index < 8; index++)
        {
            options.AddOption(new OptionDefinition(
                string.Create(CultureInfo.InvariantCulture, $"--option-{index}"),
                typeof(string),
                OptionArity.RequiredScalar));
        }

        ParseResult bounded = new CliParser().Parse(CommandModel.Create(options), ["--option-"]);
        Assert.AreEqual(5, bounded.Diagnostics[0].Suggestions.Count);
    }

    [TestMethod]
    [TestProperty(Metadata.Feature, "FEAT-08")]
    [TestProperty(Metadata.Requirement, "FEAT-08-004")]
    public void Parse_HelpBypassesConversionAndRequirednessButNotStructuralErrors()
    {
        int converterCalls = 0;
        int validatorCalls = 0;
        int crossValidatorCalls = 0;
        int defaultFactoryCalls = 0;
        ConverterRegistry converters = new ConverterRegistryBuilder()
            .Register<int>((_, _) =>
            {
                converterCalls++;
                return ValueConversion.Failure<int>("integer.invalid");
            })
            .Build();
        ValueValidator<int> validator = new((_, _) =>
        {
            validatorCalls++;
            return ValueValidationOutcome.Success;
        });
        CommandBuilder root = new CommandBuilder("tool")
            .AddChild(new CommandBuilder("copy")
                .AddOption(new OptionDefinition(
                    "--count",
                    typeof(int),
                    OptionArity.RequiredScalar,
                    isRequired: true,
                    validators: [validator]))
                .AddOption(new OptionDefinition(
                    "--label",
                    typeof(string),
                    OptionArity.RequiredScalar,
                    defaultValuePolicy: DefaultValuePolicy.Factory,
                    defaultValueFactory: () =>
                    {
                        defaultFactoryCalls++;
                        return "default";
                    }))
                .AddCrossValidator(_ =>
                {
                    crossValidatorCalls++;
                    return ValueValidationOutcome.Success;
                }));
        CommandModel model = CommandModel.Create(root, includeStandardHelp: true);
        CliParser parser = new(converters);

        ParseResult help = parser.Parse(model, ["copy", "--count=bad", "--help"]);
        ParseResult unknown = parser.Parse(model, ["copy", "--wat", "--help"]);
        ParseResult missingValue = parser.Parse(model, ["copy", "--count", "--help"]);
        ParseResult malformedHelp = parser.Parse(model, ["copy", "--help=maybe"]);
        ParseResult falseHelp = parser.Parse(model, ["copy", "--help", "false"]);
        ParseResult inlineFalseHelp = parser.Parse(model, ["copy", "--help=false"]);
        ParseResult repeatedHelp = parser.Parse(model, ["copy", "--help", "-h"]);
        ParseResult unknownCommand = parser.Parse(model, ["missing", "--help"]);

        Assert.AreEqual(ParseOutcome.HelpRequested, help.Outcome);
        CollectionAssert.AreEqual(new[] { "tool", "copy" }, help.SelectedPath.Select(static command => command.Name).ToArray());
        Assert.AreEqual(0, help.Values.Count);
        Assert.AreEqual(0, converterCalls);
        Assert.AreEqual(0, validatorCalls);
        Assert.AreEqual(0, crossValidatorCalls);
        Assert.AreEqual(0, defaultFactoryCalls);
        Assert.AreEqual(ParseDiagnosticCode.UnknownOption, unknown.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingValue, missingValue.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.InvalidValue, malformedHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, falseHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.MissingRequired, inlineFalseHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.DuplicateOption, repeatedHelp.Diagnostics[0].Code);
        Assert.AreEqual(ParseDiagnosticCode.UnknownCommand, unknownCommand.Diagnostics[0].Code);

        CommandModel positionalModel = CommandModel.Create(
            new CommandBuilder("tool").AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))),
            includeStandardHelp: true);
        ParseResult afterEndMarker = parser.Parse(positionalModel, ["--", "--help"]);
        Assert.AreEqual(ParseOutcome.Validated, afterEndMarker.Outcome);
        Assert.IsTrue(afterEndMarker.TryGetPositionalValue(
            positionalModel.Root,
            positionalModel.Root.Positionals[0],
            out IReadOnlyList<string>? paths));
        CollectionAssert.AreEqual(new[] { "--help" }, paths!.ToArray());
    }

    private static CommandModel CreateLeafModel()
    {
        return CommandModel.Create(new CommandBuilder("tool")
            .AddPositional(new PositionalDefinition(
                "paths",
                typeof(IReadOnlyList<string>),
                minimumValues: 0,
                maximumValues: null,
                elementType: typeof(string))));
    }

    private enum AccessMode
    {
        Read,
        Write
    }
}
